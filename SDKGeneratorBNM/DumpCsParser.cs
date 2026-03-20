using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace SDKGeneratorBNM
{
    internal static class DumpCsParser
    {
        private const string GenericPlaceholderPrefix = "__GEN";

        private static readonly HashSet<string> TypeModifiers = new HashSet<string>(StringComparer.Ordinal) { "public", "private", "protected", "internal", "sealed", "abstract", "static", "partial", "unsafe", "readonly", "new" };

        private static readonly HashSet<string> MethodModifiers = new HashSet<string>(StringComparer.Ordinal) { "public", "private", "protected", "internal", "static", "virtual", "override", "abstract", "sealed", "extern", "unsafe", "new", "async", "partial", "readonly" };

        private static readonly HashSet<string> FieldModifiers = new HashSet<string>(StringComparer.Ordinal) { "public", "private", "protected", "internal", "static", "readonly", "const", "volatile", "new" };

        private static readonly HashSet<string> PropertyModifiers = new HashSet<string>(StringComparer.Ordinal) { "public", "private", "protected", "internal", "static", "virtual", "override", "abstract", "sealed", "extern", "unsafe", "new" };

        private static readonly Dictionary<string, Func<ModuleDefinition, TypeReference>> SystemTypeAliases = new Dictionary<string, Func<ModuleDefinition, TypeReference>>(StringComparer.Ordinal)
        {
            { "void", m => m.TypeSystem.Void },
            { "bool", m => m.TypeSystem.Boolean },
            { "byte", m => m.TypeSystem.Byte },
            { "sbyte", m => m.TypeSystem.SByte },
            { "short", m => m.TypeSystem.Int16 },
            { "ushort", m => m.TypeSystem.UInt16 },
            { "int", m => m.TypeSystem.Int32 },
            { "uint", m => m.TypeSystem.UInt32 },
            { "long", m => m.TypeSystem.Int64 },
            { "ulong", m => m.TypeSystem.UInt64 },
            { "char", m => m.TypeSystem.Char },
            { "float", m => m.TypeSystem.Single },
            { "double", m => m.TypeSystem.Double },
            { "string", m => m.TypeSystem.String },
            { "object", m => m.TypeSystem.Object },
            { "Void", m => m.TypeSystem.Void },
            { "Boolean", m => m.TypeSystem.Boolean },
            { "Byte", m => m.TypeSystem.Byte },
            { "SByte", m => m.TypeSystem.SByte },
            { "Int16", m => m.TypeSystem.Int16 },
            { "UInt16", m => m.TypeSystem.UInt16 },
            { "Int32", m => m.TypeSystem.Int32 },
            { "UInt32", m => m.TypeSystem.UInt32 },
            { "Int64", m => m.TypeSystem.Int64 },
            { "UInt64", m => m.TypeSystem.UInt64 },
            { "Char", m => m.TypeSystem.Char },
            { "Single", m => m.TypeSystem.Single },
            { "Double", m => m.TypeSystem.Double },
            { "String", m => m.TypeSystem.String },
            { "Object", m => m.TypeSystem.Object },
            { "IntPtr", m => m.TypeSystem.IntPtr },
            { "UIntPtr", m => m.TypeSystem.UIntPtr },
        };

        private sealed class ParseState
        {
            public ParseState(ModuleDefinition module)
            {
                Module = module;
            }

            public ModuleDefinition Module { get; }
            public Dictionary<string, TypeDefinition> TypeMap { get; } = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
            public Dictionary<string, List<TypeDefinition>> NameMap { get; } = new Dictionary<string, List<TypeDefinition>>(StringComparer.Ordinal);
            public List<TypeDefinition> AllTypes { get; } = new List<TypeDefinition>();
            public Dictionary<TypeDefinition, List<string>> PendingBaseLists { get; } = new Dictionary<TypeDefinition, List<string>>();
        }

        private enum MemberSection
        {
            None,
            Fields,
            Properties,
            Methods,
        }

        private sealed class TypeDecl
        {
            public string Name;
            public string Kind;
            public List<string> BaseTypes = new List<string>();
            public int GenericArity;
            public List<string> GenericParams = new List<string>();
            public bool HasExplicitGenericArgs;
            public List<string> Modifiers = new List<string>();
        }

        public static List<TypeDefinition> ParseDump(string path, Func<TypeDefinition, bool> includeType = null)
        {
            Console.WriteLine($"Parsing dump.cs: {path}");
            var sw = Stopwatch.StartNew();
            var module = ModuleDefinition.CreateModule($"Dump_{Path.GetFileNameWithoutExtension(path)}", ModuleKind.Dll);
            var state = new ParseState(module);
            FirstPass(path, state);
            Console.WriteLine($"First pass: {state.AllTypes.Count} types ({sw.Elapsed.TotalSeconds:F1}s)");
            ResolveBaseTypes(state);
            SecondPass(path, state, includeType);
            Console.WriteLine($"Second pass done ({sw.Elapsed.TotalSeconds:F1}s)");
            return state.AllTypes;
        }

        private static void FirstPass(string path, ParseState state)
        {
            string currentNamespace = string.Empty;
            int depth = 0;
            var typeStack = new Stack<(TypeDefinition Type, int Depth)>();
            TypeDefinition pendingType = null;

            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (TryParseNamespaceComment(line, out var ns))
                {
                    currentNamespace = NormalizeNamespace(ns);
                    continue;
                }

                if (TryParseTypeDeclaration(line, out var decl))
                {
                    var parent = typeStack.Count > 0 ? typeStack.Peek().Type : null;
                    pendingType = CreateTypeDefinition(state, decl, currentNamespace, parent);
                    state.AllTypes.Add(pendingType);
                    state.PendingBaseLists[pendingType] = decl.BaseTypes;
                }

                string code = StripLineComment(rawLine);
                int opens = CountChar(code, '{');
                int closes = CountChar(code, '}');

                for (int i = 0; i < opens; i++)
                {
                    depth++;
                    if (pendingType != null)
                    {
                        typeStack.Push((pendingType, depth));
                        pendingType = null;
                    }
                }
                for (int i = 0; i < closes; i++)
                {
                    if (typeStack.Count > 0 && depth == typeStack.Peek().Depth)
                        typeStack.Pop();
                    depth--;
                }
            }
        }

        private static void ResolveBaseTypes(ParseState state)
        {
            foreach (var kvp in state.PendingBaseLists)
            {
                var type = kvp.Key;
                var bases = kvp.Value;

                if (type.IsEnum)
                {
                    type.BaseType = GetSystemType(state, "Enum");
                    continue;
                }
                if (type.IsValueType)
                {
                    type.BaseType = GetSystemType(state, "ValueType");
                    continue;
                }
                if (type.IsInterface)
                    continue;

                TypeReference baseType = null;
                if (bases != null)
                {
                    foreach (var baseName in bases)
                    {
                        if (IsInterfaceName(baseName, state))
                            continue;
                        baseType = ParseType(baseName, type, null, state);
                        break;
                    }
                }
                type.BaseType = baseType ?? state.Module.TypeSystem.Object;
            }
        }

        private static void SecondPass(string path, ParseState state, Func<TypeDefinition, bool> includeType)
        {
            string currentNamespace = string.Empty;
            int depth = 0;
            var typeStack = new Stack<(TypeDefinition Type, int Depth, bool Include)>();
            TypeDefinition pendingType = null;
            bool pendingInclude = false;
            var section = MemberSection.None;
            int lineNo = 0;
            int typeCount = 0;

            foreach (var rawLine in File.ReadLines(path))
            {
                lineNo++;
                if (lineNo % 500000 == 0)
                    Console.WriteLine($"  ...{lineNo:N0} lines processed");

                var trimmed = rawLine.Trim();
                if (TryParseNamespaceComment(trimmed, out var ns))
                {
                    currentNamespace = NormalizeNamespace(ns);
                    continue;
                }

                bool isTypeDecl = false;
                if (TryParseTypeDeclaration(trimmed, out var decl))
                {
                    typeCount++;
                    if (typeCount % 5000 == 0)
                        Console.WriteLine($"  ...{typeCount:N0} types processed");

                    var parent = typeStack.Count > 0 ? typeStack.Peek().Type : null;
                    var key = GetCSharpFullName(currentNamespace, parent, decl.Name, decl.GenericArity, decl.HasExplicitGenericArgs);
                    state.TypeMap.TryGetValue(key, out pendingType);
                    pendingInclude = pendingType != null && (includeType == null || includeType(pendingType));
                    isTypeDecl = true;
                    section = MemberSection.None;
                }

                if (!isTypeDecl && typeStack.Count > 0 && typeStack.Peek().Include)
                {
                    if (trimmed.StartsWith("// Fields", StringComparison.Ordinal))
                        section = MemberSection.Fields;
                    else if (trimmed.StartsWith("// Properties", StringComparison.Ordinal))
                        section = MemberSection.Properties;
                    else if (trimmed.StartsWith("// Methods", StringComparison.Ordinal))
                        section = MemberSection.Methods;
                    else if (trimmed.Length > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal))
                    {
                        var currentType = typeStack.Peek().Type;
                        switch (section)
                        {
                            case MemberSection.Fields:
                                ParseFieldLine(trimmed, currentType, state);
                                break;
                            case MemberSection.Properties:
                                ParsePropertyLine(trimmed, currentType, state);
                                break;
                            case MemberSection.Methods:
                                ParseMethodLine(trimmed, currentType, state);
                                break;
                        }
                    }
                }

                string code = StripLineComment(rawLine);
                int opens = CountChar(code, '{');
                int closes = CountChar(code, '}');

                for (int i = 0; i < opens; i++)
                {
                    depth++;
                    if (pendingType != null)
                    {
                        typeStack.Push((pendingType, depth, pendingInclude));
                        pendingType = null;
                        pendingInclude = false;
                    }
                }
                for (int i = 0; i < closes; i++)
                {
                    if (typeStack.Count > 0 && depth == typeStack.Peek().Depth)
                        typeStack.Pop();
                    depth--;
                }
            }
        }

        private static bool TryParseNamespaceComment(string line, out string ns)
        {
            ns = string.Empty;
            if (!line.StartsWith("// Namespace:", StringComparison.Ordinal))
                return false;
            ns = line.Substring(13).Trim();
            return true;
        }

        private static bool TryParseTypeDeclaration(string line, out TypeDecl decl)
        {
            decl = null;
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("[", StringComparison.Ordinal))
                return false;

            var tokens = SplitTopLevelTokens(line);
            if (tokens.Count == 0)
                return false;

            int kindIndex = tokens.FindIndex(t => t == "class" || t == "struct" || t == "interface" || t == "enum");
            if (kindIndex < 0)
                return false;
            if (kindIndex + 1 >= tokens.Count)
                return false;

            decl = new TypeDecl { Modifiers = tokens.Take(kindIndex).Where(t => TypeModifiers.Contains(t)).ToList(), Kind = tokens[kindIndex] };
            ParseNameAndGenerics(tokens[kindIndex + 1], decl);

            int colonIndex = line.IndexOf(':');
            if (colonIndex >= 0)
            {
                string basePart = line.Substring(colonIndex + 1);
                int braceIndex = basePart.IndexOf('{');
                if (braceIndex >= 0)
                    basePart = basePart.Substring(0, braceIndex);
                basePart = basePart.Trim();
                foreach (var b in SplitTopLevel(basePart, ','))
                {
                    var bt = b.Trim();
                    if (!string.IsNullOrEmpty(bt))
                        decl.BaseTypes.Add(bt);
                }
            }
            return true;
        }

        private static void ParseNameAndGenerics(string nameToken, TypeDecl decl)
        {
            decl.Name = nameToken;
            decl.GenericArity = 0;
            decl.GenericParams.Clear();
            decl.HasExplicitGenericArgs = false;

            int lt = nameToken.IndexOf('<');
            if (lt >= 0)
            {
                int gt = FindMatching(nameToken, lt, '<', '>');
                if (gt > lt)
                {
                    string argList = nameToken.Substring(lt + 1, gt - lt - 1);
                    var args = SplitTopLevel(argList, ',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
                    decl.GenericParams.AddRange(args);
                    decl.GenericArity = args.Count;
                    decl.Name = nameToken.Substring(0, lt).Trim();
                    decl.HasExplicitGenericArgs = true;
                    return;
                }
            }

            int backtick = nameToken.IndexOf('`');
            if (backtick >= 0)
            {
                decl.Name = nameToken;
                if (int.TryParse(nameToken.Substring(backtick + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var arity))
                    decl.GenericArity = arity;
            }
        }

        private static TypeDefinition CreateTypeDefinition(ParseState state, TypeDecl decl, string ns, TypeDefinition parent)
        {
            string typeName = decl.HasExplicitGenericArgs ? $"{decl.Name}`{decl.GenericArity}" : decl.Name;

            var attrs = TypeAttributes.Class;
            if (decl.Modifiers.Contains("public"))
                attrs |= TypeAttributes.Public;
            else
                attrs |= TypeAttributes.NotPublic;
            if (decl.Modifiers.Contains("abstract"))
                attrs |= TypeAttributes.Abstract;
            if (decl.Modifiers.Contains("sealed"))
                attrs |= TypeAttributes.Sealed;
            if (decl.Kind == "interface")
                attrs |= TypeAttributes.Interface | TypeAttributes.Abstract;
            if (decl.Kind == "struct")
                attrs |= TypeAttributes.SequentialLayout;

            var typeDef = new TypeDefinition(ns, typeName, attrs);

            if (parent != null)
            {
                typeDef.DeclaringType = parent;
                parent.NestedTypes.Add(typeDef);
            }
            else
            {
                state.Module.Types.Add(typeDef);
            }

            if (decl.GenericArity > 0)
            {
                if (decl.GenericParams.Count > 0)
                {
                    foreach (var gp in decl.GenericParams)
                        typeDef.GenericParameters.Add(new GenericParameter(gp, typeDef));
                }
                else
                {
                    for (int i = 0; i < decl.GenericArity; i++)
                        typeDef.GenericParameters.Add(new GenericParameter($"{GenericPlaceholderPrefix}{i}", typeDef));
                }
            }

            string key = GetCSharpFullName(ns, parent, decl.Name, decl.GenericArity, decl.HasExplicitGenericArgs);
            state.TypeMap[key] = typeDef;
            if (!state.NameMap.TryGetValue(typeDef.Name, out var list))
            {
                list = new List<TypeDefinition>();
                state.NameMap[typeDef.Name] = list;
            }
            list.Add(typeDef);
            return typeDef;
        }

        private static void ParseFieldLine(string line, TypeDefinition currentType, ParseState state)
        {
            string code = StripLineComment(line).Trim();
            if (string.IsNullOrEmpty(code))
                return;
            if (!code.EndsWith(";", StringComparison.Ordinal) || code.Contains("(") || code.Contains("{"))
                return;

            string valuePart = null;
            var split = SplitTopLevel(code, '=');
            if (split.Count == 2)
            {
                code = split[0].Trim();
                valuePart = split[1].Trim().TrimEnd(';').Trim();
            }
            code = code.TrimEnd(';').Trim();

            var tokens = SplitTopLevelTokens(code);
            if (tokens.Count < 2)
                return;

            int idx = 0;
            bool isStatic = false,
                isLiteral = false,
                isInitOnly = false;
            while (idx < tokens.Count && FieldModifiers.Contains(tokens[idx]))
            {
                switch (tokens[idx])
                {
                    case "static":
                        isStatic = true;
                        break;
                    case "const":
                        isLiteral = true;
                        break;
                    case "readonly":
                        isInitOnly = true;
                        break;
                }
                idx++;
            }
            if (idx + 1 >= tokens.Count)
                return;

            var fieldType = ParseType(tokens[idx], currentType, null, state);
            var attrs = FieldAttributes.Private;
            if (tokens.Contains("public"))
                attrs = FieldAttributes.Public;
            else if (tokens.Contains("protected") && tokens.Contains("internal"))
                attrs = FieldAttributes.FamORAssem;
            else if (tokens.Contains("protected"))
                attrs = FieldAttributes.Family;
            else if (tokens.Contains("internal"))
                attrs = FieldAttributes.Assembly;

            if (isStatic)
                attrs |= FieldAttributes.Static;
            if (isInitOnly)
                attrs |= FieldAttributes.InitOnly;
            if (isLiteral)
                attrs |= FieldAttributes.Literal | FieldAttributes.Static;

            var field = new FieldDefinition(tokens[idx + 1], attrs, fieldType);
            if (isLiteral && valuePart != null)
                field.Constant = ParseConstant(valuePart, fieldType);

            currentType.Fields.Add(field);
        }

        private static void ParsePropertyLine(string line, TypeDefinition currentType, ParseState state)
        {
            if (!line.Contains("{") || line.StartsWith("//", StringComparison.Ordinal))
                return;

            string code = StripLineComment(line).Trim();
            int braceIndex = code.IndexOf('{');
            if (braceIndex <= 0)
                return;

            string declPart = code.Substring(0, braceIndex).Trim();
            var tokens = SplitTopLevelTokens(declPart);
            if (tokens.Count < 2)
                return;

            int idx = 0;
            while (idx < tokens.Count && PropertyModifiers.Contains(tokens[idx]))
                idx++;
            if (idx + 1 >= tokens.Count)
                return;

            string nameToken = tokens[idx + 1];
            if (nameToken.Contains("."))
                return;

            var propType = ParseType(tokens[idx], currentType, null, state);
            var prop = new PropertyDefinition(nameToken, PropertyAttributes.None, propType);

            if (code.Contains("get;"))
            {
                var getter = new MethodDefinition($"get_{nameToken}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propType);
                prop.GetMethod = getter;
                currentType.Methods.Add(getter);
            }
            if (code.Contains("set;"))
            {
                var setter = new MethodDefinition($"set_{nameToken}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, state.Module.TypeSystem.Void);
                setter.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, propType));
                prop.SetMethod = setter;
                currentType.Methods.Add(setter);
            }

            currentType.Properties.Add(prop);
        }

        private static void ParseMethodLine(string line, TypeDefinition currentType, ParseState state)
        {
            if (line.StartsWith("//", StringComparison.Ordinal) || !line.Contains("(") || !line.Contains(")"))
                return;
            if (line.Contains(" RVA:"))
                return;

            string code = StripLineComment(line).Trim();
            if (code.EndsWith(";", StringComparison.Ordinal))
                code = code.Substring(0, code.Length - 1).Trim();
            int braceIndex = code.IndexOf('{');
            if (braceIndex >= 0)
                code = code.Substring(0, braceIndex).Trim();

            int parenIndex = code.IndexOf('(');
            if (parenIndex <= 0)
                return;

            string head = code.Substring(0, parenIndex).Trim();
            string paramList = code.Substring(parenIndex + 1);
            int closeIndex = paramList.LastIndexOf(')');
            if (closeIndex >= 0)
                paramList = paramList.Substring(0, closeIndex);

            var tokens = SplitTopLevelTokens(head);
            if (tokens.Count < 2)
                return;

            int idx = 0;
            var mods = new HashSet<string>(StringComparer.Ordinal);
            while (idx < tokens.Count && MethodModifiers.Contains(tokens[idx]))
                mods.Add(tokens[idx++]);
            if (idx + 1 >= tokens.Count)
                return;

            string nameToken = tokens[idx + 1];
            if (nameToken.Contains("."))
                return;

            int methodArity = 0;
            var methodGenericParams = new List<string>();
            if (TryParseExplicitGenericParams(nameToken, out var methodName, out var explicitParams))
            {
                methodGenericParams = explicitParams;
                methodArity = explicitParams.Count;
                nameToken = methodName;
            }
            else
            {
                int backtick = nameToken.IndexOf('`');
                if (backtick >= 0 && int.TryParse(nameToken.Substring(backtick + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var arity))
                    methodArity = arity;
            }

            var returnType = ParseType(tokens[idx], currentType, null, state);
            var attrs = MethodAttributes.HideBySig;
            if (mods.Contains("public"))
                attrs |= MethodAttributes.Public;
            else if (mods.Contains("protected") && mods.Contains("internal"))
                attrs |= MethodAttributes.FamORAssem;
            else if (mods.Contains("protected"))
                attrs |= MethodAttributes.Family;
            else if (mods.Contains("internal"))
                attrs |= MethodAttributes.Assembly;
            else
                attrs |= MethodAttributes.Private;

            if (mods.Contains("static"))
                attrs |= MethodAttributes.Static;
            if (mods.Contains("abstract"))
                attrs |= MethodAttributes.Abstract;
            if (mods.Contains("virtual") || mods.Contains("override"))
                attrs |= MethodAttributes.Virtual;

            var method = new MethodDefinition(nameToken, attrs, returnType);
            if (methodArity > 0)
            {
                if (methodGenericParams.Count > 0)
                {
                    foreach (var gp in methodGenericParams)
                        method.GenericParameters.Add(new GenericParameter(gp, method));
                }
                else
                {
                    for (int i = 0; i < methodArity; i++)
                        method.GenericParameters.Add(new GenericParameter($"{GenericPlaceholderPrefix}{i}", method));
                }
            }

            foreach (var paramText in SplitTopLevel(paramList, ','))
            {
                var pt = paramText.Trim();
                if (pt.Length == 0)
                    continue;
                var param = ParseParameter(pt, method.Parameters.Count, currentType, method, state);
                if (param != null)
                    method.Parameters.Add(param);
            }

            currentType.Methods.Add(method);
        }

        private static ParameterDefinition ParseParameter(string text, int index, TypeDefinition currentType, MethodDefinition method, ParseState state)
        {
            bool byRef = false,
                isOut = false;
            var tokens = SplitTopLevelTokens(text);
            int idx = 0;
            while (idx < tokens.Count)
            {
                switch (tokens[idx])
                {
                    case "ref":
                    case "in":
                        byRef = true;
                        idx++;
                        continue;
                    case "out":
                        byRef = true;
                        isOut = true;
                        idx++;
                        continue;
                    case "params":
                        idx++;
                        continue;
                }
                break;
            }
            if (idx >= tokens.Count)
                return null;

            string typeToken = tokens[idx];
            string nameToken = idx + 1 < tokens.Count ? tokens[idx + 1] : $"param{index}";

            var paramType = ParseType(typeToken, currentType, method, state);
            if (byRef)
                paramType = new ByReferenceType(paramType);

            var pattrs = ParameterAttributes.None;
            if (isOut)
                pattrs |= ParameterAttributes.Out;
            return new ParameterDefinition(nameToken, pattrs, paramType);
        }

        private static TypeReference ParseType(string typeStr, TypeDefinition currentType, MethodDefinition currentMethod, ParseState state)
        {
            if (string.IsNullOrWhiteSpace(typeStr))
                return state.Module.TypeSystem.Object;

            string text = typeStr.Trim();
            bool nullable = false;
            if (text.EndsWith("?", StringComparison.Ordinal))
            {
                nullable = true;
                text = text.Substring(0, text.Length - 1).Trim();
            }

            var suffixes = new List<string>(2);
            int depth = 0;
            for (int i = text.Length - 1; i >= 0; i--)
            {
                char c = text[i];
                if (c == '>')
                    depth++;
                else if (c == '<')
                    depth--;
                if (depth != 0)
                    continue;

                if (c == '*')
                {
                    suffixes.Add("*");
                    text = text.Substring(0, i).Trim();
                    i = text.Length;
                    continue;
                }
                if (c == ']')
                {
                    int lb = text.LastIndexOf('[', i);
                    if (lb >= 0)
                    {
                        suffixes.Add(text.Substring(lb, i - lb + 1));
                        text = text.Substring(0, lb).Trim();
                        i = text.Length;
                    }
                }
            }

            TypeReference core = ParseNamedType(text, currentType, currentMethod, state);

            if (nullable)
            {
                var nullableRef = new TypeReference("System", "Nullable`1", state.Module, state.Module.TypeSystem.CoreLibrary, true);
                var git = new GenericInstanceType(nullableRef);
                git.GenericArguments.Add(core);
                core = git;
            }

            for (int i = suffixes.Count - 1; i >= 0; i--)
            {
                string suf = suffixes[i];
                if (suf == "*")
                    core = new PointerType(core);
                else if (suf.StartsWith("[", StringComparison.Ordinal))
                    core = new ArrayType(core, suf.Count(ch => ch == ',') + 1);
            }

            return core;
        }

        private static TypeReference ParseNamedType(string name, TypeDefinition currentType, MethodDefinition currentMethod, ParseState state)
        {
            string clean = name.Trim();
            if (clean.StartsWith("global::", StringComparison.Ordinal))
                clean = clean.Substring(8);

            if (TryResolveGenericParameter(clean, currentType, currentMethod, out var gp))
                return gp;

            if (SystemTypeAliases.TryGetValue(clean, out var alias))
                return alias(state.Module);

            if (clean.Contains("<"))
            {
                int lt = clean.IndexOf('<');
                int gt = FindMatching(clean, lt, '<', '>');
                string baseName = clean.Substring(0, lt).Trim();
                string argList = clean.Substring(lt + 1, gt - lt - 1);
                var args = SplitTopLevel(argList, ',').Select(a => ParseType(a, currentType, currentMethod, state)).ToList();
                string lookupName = $"{baseName}`{args.Count}";
                var baseType = ResolveTypeByName(lookupName, currentType, state);
                var git = new GenericInstanceType(baseType);
                foreach (var a in args)
                    git.GenericArguments.Add(a);
                return git;
            }

            return ResolveTypeByName(clean, currentType, state);
        }

        private static TypeReference ResolveTypeByName(string name, TypeDefinition currentType, ParseState state)
        {
            if (state.TypeMap.TryGetValue(name, out var td))
                return td;

            if (currentType != null)
            {
                string ns = currentType.Namespace;
                if (!string.IsNullOrEmpty(ns) && state.TypeMap.TryGetValue(ns + "." + name, out td))
                    return td;

                var decl = currentType.DeclaringType;
                while (decl != null)
                {
                    string nested = GetCSharpFullName(ns, decl, name, 0, false);
                    if (state.TypeMap.TryGetValue(nested, out td))
                        return td;
                    decl = decl.DeclaringType;
                }
            }

            if (SystemTypeAliases.TryGetValue(name, out var sysAlias))
                return sysAlias(state.Module);

            if (!name.Contains(".") && state.NameMap.TryGetValue(name, out var matches) && matches.Count == 1)
                return matches[0];

            string nsFallback = string.Empty,
                typeName = name;
            int lastDot = name.LastIndexOf('.');
            if (lastDot >= 0)
            {
                nsFallback = name.Substring(0, lastDot);
                typeName = name.Substring(lastDot + 1);
            }
            return new TypeReference(nsFallback, typeName, state.Module, state.Module.TypeSystem.CoreLibrary);
        }

        private static bool TryResolveGenericParameter(string name, TypeDefinition currentType, MethodDefinition currentMethod, out TypeReference typeRef)
        {
            typeRef = null;
            if (currentMethod != null)
            {
                var gp = currentMethod.GenericParameters.FirstOrDefault(p => p.Name == name) ?? BindGenericPlaceholder(currentMethod.GenericParameters, name);
                if (gp != null)
                {
                    typeRef = gp;
                    return true;
                }
            }
            if (currentType != null)
            {
                var gp = currentType.GenericParameters.FirstOrDefault(p => p.Name == name) ?? BindGenericPlaceholder(currentType.GenericParameters, name);
                if (gp != null)
                {
                    typeRef = gp;
                    return true;
                }
            }
            return false;
        }

        private static GenericParameter BindGenericPlaceholder(IEnumerable<GenericParameter> parameters, string name)
        {
            if (string.IsNullOrEmpty(name) || !LooksLikeGenericParamName(name))
                return null;
            foreach (var gp in parameters)
            {
                if (gp.Name.StartsWith(GenericPlaceholderPrefix, StringComparison.Ordinal))
                {
                    gp.Name = name;
                    return gp;
                }
            }
            return null;
        }

        private static bool LooksLikeGenericParamName(string name)
        {
            if (name.Length == 1 && char.IsUpper(name[0]))
                return true;
            if (name.Length > 1 && name[0] == 'T' && char.IsUpper(name[1]))
                return true;
            return false;
        }

        private static bool TryParseExplicitGenericParams(string nameToken, out string methodName, out List<string> genParams)
        {
            methodName = nameToken;
            genParams = new List<string>();
            int lt = nameToken.IndexOf('<');
            if (lt < 0)
                return false;
            int gt = FindMatching(nameToken, lt, '<', '>');
            if (gt <= lt)
                return false;
            methodName = nameToken.Substring(0, lt);
            string args = nameToken.Substring(lt + 1, gt - lt - 1);
            genParams = SplitTopLevel(args, ',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
            return true;
        }

        private static string GetCSharpFullName(string ns, TypeDefinition parent, string name, int arity, bool hasExplicit)
        {
            string baseName = hasExplicit ? $"{name}`{arity}" : name;
            if (parent != null)
                return GetCSharpFullName(ns, parent.DeclaringType, parent.Name, 0, false) + "." + baseName;
            if (string.IsNullOrEmpty(ns) || ns == "GlobalNamespace")
                return baseName;
            return ns + "." + baseName;
        }

        private static bool IsInterfaceName(string name, ParseState state)
        {
            if (state.TypeMap.TryGetValue(name, out var td))
                return td.IsInterface;
            string baseName = name.Trim();
            int lt = baseName.IndexOf('<');
            if (lt >= 0)
                baseName = baseName.Substring(0, lt).Trim();
            int bt = baseName.IndexOf('`');
            if (bt >= 0)
                baseName = baseName.Substring(0, bt);
            return baseName.Length >= 2 && baseName[0] == 'I' && char.IsUpper(baseName[1]);
        }

        private static object ParseConstant(string value, TypeReference fieldType)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;
            string v = value.Trim();
            if (v.Equals("null", StringComparison.OrdinalIgnoreCase))
                return null;
            if (v.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (v.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            if (v.StartsWith("\"", StringComparison.Ordinal) && v.EndsWith("\"", StringComparison.Ordinal) && v.Length >= 2)
                return v.Substring(1, v.Length - 2);
            if (v.StartsWith("'", StringComparison.Ordinal) && v.EndsWith("'", StringComparison.Ordinal) && v.Length >= 2)
                return v[1];
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (long.TryParse(v.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                    return hex;
            }
            if (v.EndsWith("f", StringComparison.OrdinalIgnoreCase) && float.TryParse(v.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out var fl))
                return fl;
            if (v.EndsWith("d", StringComparison.OrdinalIgnoreCase) && double.TryParse(v.TrimEnd('d', 'D'), NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
                return db;
            if ((v.EndsWith("ul", StringComparison.OrdinalIgnoreCase) || v.EndsWith("lu", StringComparison.OrdinalIgnoreCase) || v.EndsWith("u", StringComparison.OrdinalIgnoreCase)))
            {
                var trimmed = v.TrimEnd('u', 'U', 'l', 'L');
                if (ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ul))
                    return (long)ul;
            }
            if (v.EndsWith("l", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = v.TrimEnd('l', 'L');
                if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                    return l;
            }
            if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
                return intVal;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl))
                return dbl;
            return v;
        }

        private static string NormalizeNamespace(string ns) => string.IsNullOrWhiteSpace(ns) ? "GlobalNamespace" : ns.Trim();

        private static List<string> SplitTopLevelTokens(string text)
        {
            var tokens = new List<string>(8);
            if (string.IsNullOrWhiteSpace(text))
                return tokens;
            int depth = 0,
                start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<')
                    depth++;
                else if (c == '>')
                    depth--;
                else if (char.IsWhiteSpace(c) && depth == 0)
                {
                    if (i > start)
                        tokens.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < text.Length)
                tokens.Add(text.Substring(start));
            return tokens.Where(t => t.Length > 0).ToList();
        }

        private static List<string> SplitTopLevel(string text, char separator)
        {
            var parts = new List<string>(4);
            if (string.IsNullOrWhiteSpace(text))
                return parts;
            int depth = 0,
                start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '<')
                    depth++;
                else if (c == '>')
                    depth--;
                else if (c == separator && depth == 0)
                {
                    parts.Add(text.Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(text.Substring(start));
            return parts;
        }

        private static int FindMatching(string text, int start, char open, char close)
        {
            int depth = 0;
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == open)
                    depth++;
                else if (text[i] == close)
                {
                    if (--depth == 0)
                        return i;
                }
            }
            return -1;
        }

        private static string StripLineComment(string line)
        {
            int idx = line.IndexOf("//", StringComparison.Ordinal);
            return idx >= 0 ? line.Substring(0, idx) : line;
        }

        private static TypeReference GetSystemType(ParseState state, string name) => new TypeReference("System", name, state.Module, state.Module.TypeSystem.CoreLibrary);

        private static int CountChar(string text, char ch)
        {
            int count = 0;
            foreach (var c in text)
                if (c == ch)
                    count++;
            return count;
        }
    }
}
