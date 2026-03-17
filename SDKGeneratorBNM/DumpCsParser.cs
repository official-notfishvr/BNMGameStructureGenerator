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

        private static readonly HashSet<string> TypeModifiers = new HashSet<string>
        {
            "public", "private", "protected", "internal", "sealed", "abstract", "static", "partial", "unsafe", "readonly", "new"
        };

        private static readonly HashSet<string> MethodModifiers = new HashSet<string>
        {
            "public", "private", "protected", "internal", "static", "virtual", "override", "abstract", "sealed", "extern", "unsafe", "new", "async", "partial", "readonly"
        };

        private static readonly HashSet<string> FieldModifiers = new HashSet<string>
        {
            "public", "private", "protected", "internal", "static", "readonly", "const", "volatile", "new"
        };

        private static readonly HashSet<string> PropertyModifiers = new HashSet<string>
        {
            "public", "private", "protected", "internal", "static", "virtual", "override", "abstract", "sealed", "extern", "unsafe", "new"
        };

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
            public List<TypeDefinition> AllTypes { get; } = new List<TypeDefinition>();
            public Dictionary<TypeDefinition, List<string>> PendingBaseLists { get; } = new Dictionary<TypeDefinition, List<string>>();
        }

        public static List<TypeDefinition> ParseDump(string path)
        {
            Console.WriteLine($"[INFO] Parsing dump.cs: {path}");
            var sw = Stopwatch.StartNew();
            var module = ModuleDefinition.CreateModule($"Dump_{Path.GetFileNameWithoutExtension(path)}", ModuleKind.Dll);
            var state = new ParseState(module);
            FirstPass(path, state);
            Console.WriteLine($"[INFO] dump.cs first pass: {state.AllTypes.Count} types ({sw.Elapsed})");
            ResolveBaseTypes(state);
            SecondPass(path, state);
            Console.WriteLine($"[INFO] dump.cs second pass done ({sw.Elapsed})");
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
                    var typeDef = CreateTypeDefinition(state, decl, currentNamespace, parent);
                    state.AllTypes.Add(typeDef);
                    state.PendingBaseLists[typeDef] = decl.BaseTypes;
                    pendingType = typeDef;
                }

                var code = StripLineComment(rawLine);
                var openCount = CountChar(code, '{');
                var closeCount = CountChar(code, '}');

                for (int i = 0; i < openCount; i++)
                {
                    depth++;
                    if (pendingType != null)
                    {
                        typeStack.Push((pendingType, depth));
                        pendingType = null;
                    }
                }

                for (int i = 0; i < closeCount; i++)
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
                if (type.IsValueType && !type.IsEnum)
                {
                    type.BaseType = GetSystemType(state, "ValueType");
                    continue;
                }
                if (type.IsInterface)
                    continue;

                TypeReference baseType = null;
                if (bases != null && bases.Count > 0)
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

        private static void SecondPass(string path, ParseState state)
        {
            string currentNamespace = string.Empty;
            int depth = 0;
            var typeStack = new Stack<(TypeDefinition Type, int Depth)>();
            TypeDefinition pendingType = null;
            var section = MemberSection.None;
            int lineNo = 0;
            int typeCount = 0;

            foreach (var rawLine in File.ReadLines(path))
            {
                lineNo++;
                if (lineNo % 200000 == 0)
                    Console.WriteLine($"[INFO] dump.cs second pass lines: {lineNo}");
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
                    if (typeCount % 2000 == 0)
                        Console.WriteLine($"[INFO] dump.cs second pass types: {typeCount}");
                    var parent = typeStack.Count > 0 ? typeStack.Peek().Type : null;
                    var key = GetCSharpFullName(currentNamespace, parent, decl.Name, decl.GenericArity, decl.HasExplicitGenericArgs);
                    if (!state.TypeMap.TryGetValue(key, out var typeDef))
                        typeDef = null;
                    pendingType = typeDef;
                    isTypeDecl = true;
                    section = MemberSection.None;
                }

                if (!isTypeDecl && typeStack.Count > 0)
                {
                    if (trimmed.StartsWith("// Fields"))
                    {
                        section = MemberSection.Fields;
                    }
                    else if (trimmed.StartsWith("// Properties"))
                    {
                        section = MemberSection.Properties;
                    }
                    else if (trimmed.StartsWith("// Methods"))
                    {
                        section = MemberSection.Methods;
                    }
                    else if (!string.IsNullOrWhiteSpace(trimmed) && !trimmed.StartsWith("//"))
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

                var code = StripLineComment(rawLine);
                var openCount = CountChar(code, '{');
                var closeCount = CountChar(code, '}');

                for (int i = 0; i < openCount; i++)
                {
                    depth++;
                    if (pendingType != null)
                    {
                        typeStack.Push((pendingType, depth));
                        pendingType = null;
                    }
                }

                for (int i = 0; i < closeCount; i++)
                {
                    if (typeStack.Count > 0 && depth == typeStack.Peek().Depth)
                        typeStack.Pop();
                    depth--;
                }
            }
        }

        private enum MemberSection
        {
            None,
            Fields,
            Properties,
            Methods
        }

        private sealed class TypeDecl
        {
            public string Name { get; set; }
            public string Kind { get; set; }
            public List<string> BaseTypes { get; set; } = new List<string>();
            public int GenericArity { get; set; }
            public List<string> GenericParams { get; set; } = new List<string>();
            public bool HasExplicitGenericArgs { get; set; }
            public List<string> Modifiers { get; set; } = new List<string>();
        }

        private static bool TryParseNamespaceComment(string line, out string ns)
        {
            ns = string.Empty;
            if (!line.StartsWith("// Namespace:"))
                return false;
            ns = line.Substring("// Namespace:".Length).Trim();
            return true;
        }

        private static bool TryParseTypeDeclaration(string line, out TypeDecl decl)
        {
            decl = null;
            if (string.IsNullOrWhiteSpace(line))
                return false;
            if (line.StartsWith("//") || line.StartsWith("["))
                return false;

            var tokens = SplitTopLevelTokens(line);
            if (tokens.Count == 0)
                return false;

            int kindIndex = tokens.FindIndex(t => t == "class" || t == "struct" || t == "interface" || t == "enum");
            if (kindIndex == -1)
                return false;

            decl = new TypeDecl();
            decl.Modifiers = tokens.Take(kindIndex).Where(t => TypeModifiers.Contains(t)).ToList();
            decl.Kind = tokens[kindIndex];
            if (kindIndex + 1 >= tokens.Count)
                return false;

            string nameToken = tokens[kindIndex + 1];
            ParseNameAndGenerics(nameToken, decl);

            int colonIndex = line.IndexOf(':');
            if (colonIndex >= 0)
            {
                string basePart = line.Substring(colonIndex + 1).Trim();
                int braceIndex = basePart.IndexOf('{');
                if (braceIndex >= 0)
                    basePart = basePart.Substring(0, braceIndex).Trim();
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
                    string baseName = nameToken.Substring(0, lt).Trim();
                    string argList = nameToken.Substring(lt + 1, gt - lt - 1);
                    var args = SplitTopLevel(argList, ',').Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
                    decl.GenericParams.AddRange(args);
                    decl.GenericArity = args.Count;
                    decl.Name = baseName;
                    decl.HasExplicitGenericArgs = true;
                    return;
                }
            }

            int backtickIndex = nameToken.IndexOf('`');
            if (backtickIndex >= 0)
            {
                decl.Name = nameToken;
                if (int.TryParse(nameToken.Substring(backtickIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var arity))
                    decl.GenericArity = arity;
            }
        }

        private static TypeDefinition CreateTypeDefinition(ParseState state, TypeDecl decl, string ns, TypeDefinition parent)
        {
            string typeName = decl.Name;
            if (decl.HasExplicitGenericArgs)
                typeName = $"{decl.Name}`{decl.GenericArity}";

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
            return typeDef;
        }
        private static void ParseFieldLine(string line, TypeDefinition currentType, ParseState state)
        {
            string code = StripLineComment(line).Trim();
            if (string.IsNullOrEmpty(code))
                return;
            if (!code.EndsWith(";") || code.Contains("(") || code.Contains("{"))
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
            bool isStatic = false;
            bool isLiteral = false;
            bool isInitOnly = false;
            while (idx < tokens.Count && FieldModifiers.Contains(tokens[idx]))
            {
                var mod = tokens[idx];
                if (mod == "static")
                    isStatic = true;
                else if (mod == "const")
                    isLiteral = true;
                else if (mod == "readonly")
                    isInitOnly = true;
                idx++;
            }

            if (idx + 1 >= tokens.Count)
                return;

            string typeToken = tokens[idx];
            string nameToken = tokens[idx + 1];

            var fieldType = ParseType(typeToken, currentType, null, state);
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

            var field = new FieldDefinition(nameToken, attrs, fieldType);
            if (isLiteral && valuePart != null)
                field.Constant = ParseConstant(valuePart, fieldType);

            currentType.Fields.Add(field);
        }

        private static void ParsePropertyLine(string line, TypeDefinition currentType, ParseState state)
        {
            if (!line.Contains("{"))
                return;
            if (line.StartsWith("//"))
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

            string typeToken = tokens[idx];
            string nameToken = tokens[idx + 1];
            if (nameToken.Contains("."))
                return;

            var propType = ParseType(typeToken, currentType, null, state);
            var prop = new PropertyDefinition(nameToken, PropertyAttributes.None, propType);

            bool hasGet = code.Contains("get;");
            bool hasSet = code.Contains("set;");

            if (hasGet)
            {
                var getter = new MethodDefinition($"get_{nameToken}", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, propType);
                prop.GetMethod = getter;
                currentType.Methods.Add(getter);
            }
            if (hasSet)
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
            if (line.StartsWith("//") || !line.Contains("(") || !line.Contains(")"))
                return;
            if (line.Contains(" RVA:"))
                return;

            string code = StripLineComment(line).Trim();
            if (code.EndsWith(";"))
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
            var mods = new HashSet<string>();
            while (idx < tokens.Count && MethodModifiers.Contains(tokens[idx]))
            {
                mods.Add(tokens[idx]);
                idx++;
            }
            if (idx + 1 >= tokens.Count)
                return;

            string returnTypeToken = tokens[idx];
            string nameToken = tokens[idx + 1];
            if (nameToken.Contains("."))
                return;

            int methodArity = 0;
            List<string> methodGenericParams = new List<string>();
            if (TryParseExplicitGenericParams(nameToken, out var methodName, out var explicitParams))
            {
                methodGenericParams = explicitParams;
                methodArity = explicitParams.Count;
                nameToken = methodName;
            }
            else
            {
                int backtickIndex = nameToken.IndexOf('`');
                if (backtickIndex >= 0 && int.TryParse(nameToken.Substring(backtickIndex + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var arity))
                    methodArity = arity;
            }

            var returnType = ParseType(returnTypeToken, currentType, null, state);
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
            if (mods.Contains("virtual"))
                attrs |= MethodAttributes.Virtual;
            if (mods.Contains("override"))
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

            var paramTokens = SplitTopLevel(paramList, ',');
            int paramIndex = 0;
            foreach (var p in paramTokens)
            {
                var paramText = p.Trim();
                if (string.IsNullOrEmpty(paramText))
                    continue;
                var param = ParseParameter(paramText, paramIndex++, currentType, method, state);
                if (param != null)
                    method.Parameters.Add(param);
            }

            currentType.Methods.Add(method);
        }

        private static ParameterDefinition ParseParameter(string text, int index, TypeDefinition currentType, MethodDefinition method, ParseState state)
        {
            bool byRef = false;
            bool isOut = false;
            bool isIn = false;

            var tokens = SplitTopLevelTokens(text);
            int idx = 0;
            while (idx < tokens.Count)
            {
                string t = tokens[idx];
                if (t == "ref" || t == "out" || t == "in")
                {
                    byRef = true;
                    isOut |= t == "out";
                    isIn |= t == "in";
                    idx++;
                    continue;
                }
                if (t == "params")
                {
                    idx++;
                    continue;
                }
                break;
            }

            if (idx >= tokens.Count)
                return null;

            string typeToken;
            string nameToken;
            if (idx + 1 >= tokens.Count)
            {
                typeToken = tokens[idx];
                nameToken = $"param{index}";
            }
            else
            {
                typeToken = tokens[idx];
                nameToken = tokens[idx + 1];
            }

            var paramType = ParseType(typeToken, currentType, method, state);
            if (byRef)
                paramType = new ByReferenceType(paramType);

            var attrs = ParameterAttributes.None;
            if (isOut)
                attrs |= ParameterAttributes.Out;

            return new ParameterDefinition(nameToken, attrs, paramType);
        }
        private static TypeReference ParseType(string typeStr, TypeDefinition currentType, MethodDefinition currentMethod, ParseState state)
        {
            if (string.IsNullOrWhiteSpace(typeStr))
                return state.Module.TypeSystem.Object;

            string text = typeStr.Trim();
            bool nullable = false;
            if (text.EndsWith("?"))
            {
                nullable = true;
                text = text.Substring(0, text.Length - 1).Trim();
            }

            var suffixes = new List<string>();
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
                var nullableType = new TypeReference("System", "Nullable`1", state.Module, state.Module.TypeSystem.CoreLibrary, true);
                var git = new GenericInstanceType(nullableType);
                git.GenericArguments.Add(core);
                core = git;
            }

            for (int i = suffixes.Count - 1; i >= 0; i--)
            {
                var suf = suffixes[i];
                if (suf == "*")
                {
                    core = new PointerType(core);
                }
                else if (suf.StartsWith("["))
                {
                    int rank = suf.Count(ch => ch == ',') + 1;
                    core = new ArrayType(core, rank);
                }
            }

            return core;
        }

        private static TypeReference ParseNamedType(string name, TypeDefinition currentType, MethodDefinition currentMethod, ParseState state)
        {
            string clean = name.Trim();
            if (clean.StartsWith("global::"))
                clean = clean.Substring("global::".Length);

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
                var ns = currentType.Namespace;
                if (!string.IsNullOrEmpty(ns))
                {
                    var full = ns + "." + name;
                    if (state.TypeMap.TryGetValue(full, out td))
                        return td;
                }
                var decl = currentType.DeclaringType;
                while (decl != null)
                {
                    var nested = GetCSharpFullName(ns, decl, name, 0, false);
                    if (state.TypeMap.TryGetValue(nested, out td))
                        return td;
                    decl = decl.DeclaringType;
                }
            }

            if (SystemTypeAliases.TryGetValue(name, out var alias))
                return alias(state.Module);

            if (!name.Contains("."))
            {
                var matches = state.TypeMap.Values.Where(t => t.Name == name).Take(2).ToList();
                if (matches.Count == 1)
                    return matches[0];
            }

            string nsFallback = string.Empty;
            string typeName = name;
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
                var gp = currentMethod.GenericParameters.FirstOrDefault(p => p.Name == name);
                if (gp != null)
                {
                    typeRef = gp;
                    return true;
                }
                gp = BindGenericPlaceholder(currentMethod.GenericParameters, name);
                if (gp != null)
                {
                    typeRef = gp;
                    return true;
                }
            }

            if (currentType != null)
            {
                var gp = currentType.GenericParameters.FirstOrDefault(p => p.Name == name);
                if (gp != null)
                {
                    typeRef = gp;
                    return true;
                }
                gp = BindGenericPlaceholder(currentType.GenericParameters, name);
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
            if (name.StartsWith("T", StringComparison.Ordinal) && name.Length > 1 && char.IsUpper(name[1]))
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
            string baseName = name;
            if (hasExplicit)
                baseName = $"{name}`{arity}";
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
            int backtick = baseName.IndexOf('`');
            if (backtick >= 0)
                baseName = baseName.Substring(0, backtick);
            if (baseName.Length >= 2 && baseName[0] == 'I' && char.IsUpper(baseName[1]))
                return true;
            return false;
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

            if (v.StartsWith("\"") && v.EndsWith("\"") && v.Length >= 2)
                return v.Substring(1, v.Length - 2);
            if (v.StartsWith("'") && v.EndsWith("'") && v.Length >= 2)
                return v[1];

            bool isHex = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (isHex)
            {
                if (long.TryParse(v.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexVal))
                    return hexVal;
            }

            if (v.EndsWith("f", StringComparison.OrdinalIgnoreCase) && float.TryParse(v.TrimEnd('f', 'F'), NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                return f;
            if (v.EndsWith("d", StringComparison.OrdinalIgnoreCase) && double.TryParse(v.TrimEnd('d', 'D'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;

            if (v.EndsWith("u", StringComparison.OrdinalIgnoreCase) || v.EndsWith("ul", StringComparison.OrdinalIgnoreCase) || v.EndsWith("lu", StringComparison.OrdinalIgnoreCase))
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

            if (v.Contains(".") || v.Contains("e") || v.Contains("E"))
            {
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl))
                    return dbl;
            }

            if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal))
                return intVal;

            return v;
        }

        private static string NormalizeNamespace(string ns)
        {
            if (string.IsNullOrWhiteSpace(ns))
                return "GlobalNamespace";
            return ns.Trim();
        }

        private static List<string> SplitTopLevelTokens(string text)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return tokens;
            int depth = 0;
            int start = 0;
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
            return tokens.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        }

        private static List<string> SplitTopLevel(string text, char separator)
        {
            var parts = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return parts;
            int depth = 0;
            int start = 0;
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
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }
            return -1;
        }

        private static string StripLineComment(string line)
        {
            int idx = line.IndexOf("//", StringComparison.Ordinal);
            if (idx >= 0)
                return line.Substring(0, idx);
            return line;
        }

        private static TypeReference GetSystemType(ParseState state, string name)
        {
            return new TypeReference("System", name, state.Module, state.Module.TypeSystem.CoreLibrary);
        }

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
