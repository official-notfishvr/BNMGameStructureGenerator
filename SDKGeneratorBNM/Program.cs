using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace SDKGeneratorBNM
{
    internal static class Program
    {
        public static readonly HashSet<string> DefinedTypes = new HashSet<string>(StringComparer.Ordinal);

        private static readonly HashSet<string> DllPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> DumpPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private const string OutputExtension = ".hpp";

        private static readonly HashSet<string> OperatorNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "op_Implicit",
            "op_Explicit",
            "op_Assign",
            "op_AdditionAssignment",
            "op_SubtractionAssignment",
            "op_MultiplicationAssignment",
            "op_DivisionAssignment",
            "op_ModulusAssignment",
            "op_BitwiseAndAssignment",
            "op_BitwiseOrAssignment",
            "op_ExclusiveOrAssignment",
            "op_LeftShiftAssignment",
            "op_RightShiftAssignment",
            "op_Increment",
            "op_Decrement",
            "op_UnaryPlus",
            "op_UnaryNegation",
            "op_Addition",
            "op_Subtraction",
            "op_Multiply",
            "op_Division",
            "op_Modulus",
            "op_OnesComplement",
            "op_BitwiseAnd",
            "op_BitwiseOr",
            "op_ExclusiveOr",
            "op_LeftShift",
            "op_RightShift",
            "op_LogicalNot",
            "op_LogicalAnd",
            "op_LogicalOr",
            "op_Equality",
            "op_Inequality",
            "op_LessThan",
            "op_GreaterThan",
            "op_LessThanOrEqual",
            "op_GreaterThanOrEqual",
            "op_Comma",
            "op_True",
            "op_False",
        };

        private static readonly HashSet<string> ReservedMemberNames = new HashSet<string>(StringComparer.Ordinal) { "GetClass", "GetType", "ToString", "Equals", "GetHashCode", "MemberwiseClone", "Finalize", "NewArray", "NewList" };

        static int Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("BNM SDK Generator");
            Console.ResetColor();

            var opts = ParseArgs(args);
            if (opts == null)
            {
                PrintHelp();
                return 0;
            }

            ApplyConfig(opts);
            ResolveInputs(opts);

            if (DllPaths.Count == 0 && DumpPaths.Count == 0)
            {
                Err("No valid DLLs or dump.cs files found. Use --help for usage.");
                return 1;
            }

            Directory.CreateDirectory("./Files");
            Directory.CreateDirectory(Config.OutputDir);

            try
            {
                var types = LoadTypes();
                if (types.Count == 0)
                {
                    Err("No valid types found.");
                    return 1;
                }

                Ok($"Processing {types.Count} types...");
                ProcessTypes(types);
                Ok("Generation complete.");
                return 0;
            }
            catch (Exception ex)
            {
                Err($"Fatal: {ex.Message}");
                File.WriteAllText("GeneratorError.txt", ex.ToString());
                return 2;
            }
        }

        private sealed class Options
        {
            public bool SingleFile;
            public bool GetterSetter;
            public bool Accessor;
            public bool BnmResolve;
            public string OutputDir;
            public readonly List<string> Inputs = new List<string>();
        }

        private static Options ParseArgs(string[] args)
        {
            if (args.Length == 0 || Array.Exists(args, a => a == "--help" || a == "-h"))
                return null;

            var opts = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "-s":
                    case "--single-file":
                        opts.SingleFile = true;
                        break;
                    case "-g":
                    case "--getter-setter":
                        opts.GetterSetter = true;
                        break;
                    case "-a":
                    case "--accessor":
                        opts.Accessor = true;
                        break;
                    case "-b":
                    case "--bnm-resolve":
                        opts.BnmResolve = true;
                        break;
                    case "-o":
                    case "--output":
                        if (i + 1 < args.Length)
                            opts.OutputDir = args[++i];
                        break;
                    case "-f":
                    case "--file":
                        while (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                            opts.Inputs.Add(args[++i]);
                        break;
                    default:
                        if (!arg.StartsWith("-", StringComparison.Ordinal))
                            opts.Inputs.Add(arg);
                        break;
                }
            }
            return opts;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(
                @"
Usage:
  SDKGeneratorBNM [inputs...] [options]

Inputs:
  <path>              DLL, dump.cs, or directory (auto-detects Assembly-CSharp.dll / dump.cs)
                      Falls back to ./Files/<name>.dll or ./Files/<name>.cs

Options:
  -s, --single-file   Write all types to a single SDK.hpp instead of per-file
  -g, --getter-setter Use get_/set_ naming style instead of Get/Set
  -a, --accessor      Accessor style: returns BNM::Field<T>* / BNM::Method<T>*
  -b, --bnm-resolve   Use BNMResolve types (Transform*, GameObject*, etc.)
  -o, --output <dir>  Output directory (default: SDK)
  -f, --file <paths>  Explicitly specify one or more input files
  -h, --help          Show this help

Examples:
  SDKGeneratorBNM Assembly-CSharp.dll
  SDKGeneratorBNM dump.cs -s -g -o MySDK
  SDKGeneratorBNM MyGame/ --bnm-resolve --accessor
"
            );
        }

        private static void ApplyConfig(Options opts)
        {
            if (opts.GetterSetter)
                Config.MethodNamingStyle = Config.NamingStyle.GetterSetter;
            if (opts.Accessor)
                Config.MethodAccessorStyle = Config.MethodStyle.Accessor;
            if (opts.BnmResolve)
                Config.UseBNMResolve = true;
            if (opts.SingleFile)
                Config.SingleFile = true;
            if (!string.IsNullOrEmpty(opts.OutputDir))
                Config.OutputDir = opts.OutputDir;
        }

        private static void ResolveInputs(Options opts)
        {
            var inputs = opts.Inputs.Count > 0 ? opts.Inputs : new List<string> { "Assembly-CSharp.dll" };
            foreach (var input in inputs)
                TryAddInput(input);
        }

        private static bool TryAddInput(string path)
        {
            if (TryAddDirect(path))
                return true;

            string inFiles = Path.Combine("./Files", path);
            if (TryAddDirect(inFiles))
                return true;

            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (TryAddDirect(Path.Combine("./Files", path + ".dll")))
                    return true;
                if (TryAddDirect(Path.Combine("./Files", path + ".cs")))
                    return true;
            }

            Err($"Not found: {path}");
            return false;
        }

        private static bool TryAddDirect(string path)
        {
            if (Directory.Exists(path))
            {
                string dll = Path.Combine(path, "Assembly-CSharp.dll");
                string dump = Path.Combine(path, "dump.cs");
                if (File.Exists(dll))
                {
                    DllPaths.Add(dll);
                    return true;
                }
                if (File.Exists(dump))
                {
                    DumpPaths.Add(dump);
                    return true;
                }
                return false;
            }
            if (File.Exists(path))
            {
                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    DllPaths.Add(path);
                    return true;
                }
                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    DumpPaths.Add(path);
                    return true;
                }
            }
            return false;
        }

        private static List<TypeDefinition> LoadTypes()
        {
            var allTypes = new List<TypeDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var dllPath in DllPaths)
            {
                try
                {
                    var resolver = new DefaultAssemblyResolver();
                    foreach (var p in DllPaths)
                    {
                        string d = Path.GetDirectoryName(Path.GetFullPath(p));
                        if (d != null)
                            resolver.AddSearchDirectory(d);
                    }
                    var module = ModuleDefinition.ReadModule(
                        dllPath,
                        new ReaderParameters
                        {
                            AssemblyResolver = resolver,
                            ReadingMode = ReadingMode.Deferred,
                            ReadSymbols = false,
                        }
                    );
                    foreach (var type in module.Types)
                        AddTypesRecursive(type, allTypes, seen);
                }
                catch (Exception ex)
                {
                    Warn($"Could not load {Path.GetFileName(dllPath)}: {ex.Message}");
                }
            }

            foreach (var dumpPath in DumpPaths)
            {
                try
                {
                    var dumpTypes = DumpCsParser.ParseDump(dumpPath, IsValidType);
                    foreach (var type in dumpTypes)
                        AddTypeIfValid(type, allTypes, seen);
                }
                catch (Exception ex)
                {
                    Warn($"Could not parse {Path.GetFileName(dumpPath)}: {ex.Message}");
                }
            }

            return allTypes;
        }

        private static void AddTypesRecursive(TypeDefinition type, List<TypeDefinition> list, HashSet<string> seen)
        {
            if (type == null)
                return;
            AddTypeIfValid(type, list, seen);
            foreach (var nt in type.NestedTypes)
                AddTypesRecursive(nt, list, seen);
        }

        private static void AddTypeIfValid(TypeDefinition type, List<TypeDefinition> list, HashSet<string> seen)
        {
            if (type == null || !IsValidType(type))
                return;
            string fn = type.FullName;
            if (!string.IsNullOrEmpty(fn) && seen.Add(fn))
                list.Add(type);
        }

        private static bool IsValidType(TypeDefinition type)
        {
            if (type == null || type.IsInterface)
                return false;
            if (type.Name.StartsWith("<", StringComparison.Ordinal))
                return false;
            if (type.FullName?.StartsWith("<PrivateImplementationDetails", StringComparison.Ordinal) == true)
                return false;
            string ns = Utils.GetNamespace(type);
            if (Utils.IsSystemNamespace(ns))
                return false;
            if (Utils.IsUnityNamespace(ns))
                return false;
            return true;
        }

        private static void ProcessTypes(List<TypeDefinition> types)
        {
            if (!Config.SingleFile && Directory.Exists(Config.OutputDir))
                Directory.Delete(Config.OutputDir, true);
            Directory.CreateDirectory(Config.OutputDir);

            foreach (var t in types)
                DefinedTypes.Add(t.FullName);

            var grouped = types.GroupBy(t => Utils.FixNamespace(Utils.GetNamespace(t))).OrderBy(g => g.Key).ToArray();

            if (Config.SingleFile)
                GenerateSingleFile(grouped);
            else
                GenerateMultiFile(grouped);
        }

        private static void GenerateSingleFile(IGrouping<string, TypeDefinition>[] grouped)
        {
            var cw = new CodeWriter();
            var emittedEnums = new HashSet<string>();
            var emittedTypes = new HashSet<string>();

            cw.Line();
            foreach (var g in grouped)
                WriteForwardDecls(cw, g, new HashSet<string>(), new HashSet<string>());

            int count = 0;
            foreach (var g in grouped)
            {
                cw.StartNamespace(g.Key);
                foreach (var type in g.OrderBy(t => t.Name))
                {
                    if (!emittedTypes.Add(type.FullName))
                        continue;
                    count++;
                    if (type.IsEnum)
                    {
                        string n = Utils.FormatTypeNameForStruct(type);
                        if (emittedEnums.Add(n))
                            GenerateEnum(type, cw);
                    }
                    else
                        GenerateClass(type, cw);
                }
                cw.EndNamespace();
                cw.Line();
            }

            cw.Save(Path.Combine(Config.OutputDir, "SDK" + OutputExtension));
            Ok($"Written {count} types to SDK.hpp");
        }

        private static void GenerateMultiFile(IGrouping<string, TypeDefinition>[] grouped)
        {
            int count = 0;
            foreach (var g in grouped)
            {
                string dir = Path.Combine(Config.OutputDir, g.Key);
                foreach (var type in g.OrderBy(t => t.Name))
                {
                    var cw = new CodeWriter();
                    cw.StartNamespace(Utils.GetNamespace(type));
                    if (type.IsEnum)
                        GenerateEnum(type, cw);
                    else
                        GenerateClass(type, cw);
                    cw.EndNamespace();
                    string name = Utils.FormatTypeNameForStruct(type);
                    cw.Save(Path.Combine(dir, name + OutputExtension));
                    count++;
                }
            }

            GenerateForwardDeclFile(grouped);
            Ok($"Written {count} header files.");
        }

        private static void GenerateForwardDeclFile(IGrouping<string, TypeDefinition>[] grouped)
        {
            var cw = new CodeWriter();
            var emittedEnums = new HashSet<string>();
            var emittedTypes = new HashSet<string>();

            cw.Line();
            foreach (var g in grouped)
                WriteForwardDecls(cw, g, emittedEnums, emittedTypes);

            cw.Line("namespace BNM::Structures::Mono {");
            cw.Line("    template <typename ...Parameters> struct Func : public MulticastDelegate<void> {};");
            cw.Line("}");
            cw.Line();
            cw.Line("namespace System {");
            cw.Line("    typedef ::BNM::Structures::Mono::Action<> Action;");
            cw.Line("    template <typename ...T> using ActionT = ::BNM::Structures::Mono::Action<T...>;");
            cw.Line("    template <typename ...T> using Func    = ::BNM::Structures::Mono::Func<T...>;");
            cw.Line("}");
            cw.Line();

            cw.Save(Path.Combine(Config.OutputDir, "ForwardDeclarations.hpp"));
        }

        private static void WriteForwardDecls(CodeWriter cw, IGrouping<string, TypeDefinition> g, HashSet<string> emittedEnums, HashSet<string> emittedTypes)
        {
            cw.StartNamespace(g.Key);
            foreach (var type in g.OrderBy(t => t.Name))
            {
                if (!emittedTypes.Add(type.FullName))
                    continue;
                string name = Utils.FormatInvalidName(Utils.FormatTypeNameForStruct(type));
                if (type.IsEnum)
                {
                    if (emittedEnums.Add(name))
                        cw.Line($"enum class {name} : {Utils.GetEnumUnderlyingType(type)};");
                }
                else if (type.HasGenericParameters)
                {
                    string tparams = string.Join(", ", type.GenericParameters.Select(p => $"typename {Utils.FormatInvalidName(p.Name)}"));
                    cw.Line($"template <{tparams}> struct {name};");
                }
                else
                {
                    cw.Line($"struct {name};");
                }
            }
            cw.EndNamespace();
            cw.Line();
        }

        private static void GenerateClass(TypeDefinition type, CodeWriter cw)
        {
            try
            {
                string name = Utils.FormatInvalidName(Utils.FormatTypeNameForStruct(type));
                string bc = GetBaseClass(type, cw.Imports);
                var gns = new HashSet<string>(ReservedMemberNames, StringComparer.Ordinal);

                if (type.HasGenericParameters)
                {
                    string tparams = string.Join(", ", type.GenericParameters.Select(p => $"typename {Utils.FormatInvalidName(p.Name)}"));
                    cw.Line($"template <{tparams}>");
                }

                string inheritance = string.IsNullOrEmpty(bc) ? " : BNM::UnityEngine::MonoBehaviour" : bc;
                cw.Line($"struct {name}{inheritance} {{");
                cw.Line("public:");
                cw.Indent();

                cw.Line("static BNM::Class GetClass() {");
                cw.Indent();
                cw.Line($"static BNM::Class clazz = {Utils.GetClassGetter(type)};");
                cw.Line("return clazz;");
                cw.Unindent();
                cw.Line("}");
                cw.Line();
                cw.Line("static BNM::MonoType* GetType() { return GetClass().GetMonoType(); }");
                cw.Line();

                GenerateSingletonMethods(type, cw, gns);
                GenerateConstants(type, cw, gns);
                GeneratePropertyMethods(type, cw, gns);
                GenerateEventMethods(type, cw, gns);

                var fields = type.Fields.Where(f => !f.IsLiteral && !f.Name.Contains("<")).OrderBy(f => f.Name).ToArray();
                foreach (var f in fields)
                    GenerateFieldGetter(f, cw, type, gns);
                foreach (var f in fields)
                    GenerateFieldSetter(f, cw, type, gns);

                GenerateMethodDeclarations(type, cw, gns);

                cw.Unindent();
                cw.Line("};");
            }
            catch { }
        }

        private static void GenerateEnum(TypeDefinition type, CodeWriter cw)
        {
            try
            {
                string name = Utils.FormatInvalidName(Utils.FormatTypeNameForStruct(type));
                cw.Line($"enum class {name} : {Utils.GetEnumUnderlyingType(type)} {{");
                cw.Indent();
                var used = new HashSet<string>(StringComparer.Ordinal);
                foreach (var f in type.Fields.Where(f => f.IsStatic && f.IsLiteral))
                {
                    string fn = f.Name,
                        un = fn;
                    int s = 1;
                    while (!used.Add(un))
                        un = $"{fn}_{s++}";
                    string val = f.Constant != null ? Convert.ToInt64(f.Constant).ToString() : "0";
                    string fmt = Utils.FormatInvalidName(un);
                    if (!fmt.Equals("delete", StringComparison.OrdinalIgnoreCase))
                        cw.Line($"{fmt} = {val},");
                }
                cw.Unindent();
                cw.Line("};");
            }
            catch { }
        }

        private static string GetBaseClass(TypeDefinition type, HashSet<TypeDefinition> deps)
        {
            var bt = type.BaseType;
            if (bt == null)
                return string.Empty;

            string btFull = bt.FullName;
            if (btFull == "System.Object" || btFull == "System.ValueType" || btFull == "System.Enum")
                return string.Empty;

            if (Utils.IsSystemNamespace(bt.Namespace ?? string.Empty))
                return string.Empty;

            var res = bt.Resolve();

            if (bt.Namespace?.StartsWith("UnityEngine", StringComparison.Ordinal) == true)
            {
                string btn = Utils.CleanTypeName(bt.Name);
                return btn switch
                {
                    "MonoBehaviour" or "Object" or "Component" => $" : BNM::UnityEngine::{btn}",
                    "Behaviour" => " : BNM::UnityEngine::MonoBehaviour",
                    _ => " : BNM::UnityEngine::Object",
                };
            }

            if (res != null && Utils.ShouldAddDependency(res, type, true))
                deps?.Add(res);

            if (res != null && DefinedTypes.Contains(res.FullName))
            {
                string ns = Utils.FixNamespace(Utils.GetNamespace(res));
                string bName = Utils.FormatTypeNameForStruct(res);
                if (bt is GenericInstanceType git)
                {
                    string args = string.Join(", ", git.GenericArguments.Select(a => Utils.GetCppType(a, type, deps)));
                    return $" : ::{ns}::{bName}<{args}>";
                }
                return $" : ::{ns}::{bName}";
            }
            return string.Empty;
        }

        private static void GenerateSingletonMethods(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            var instProp = type.Properties.FirstOrDefault(p => p.Name == "Instance" && p.GetMethod != null);
            var instField = type.Fields.FirstOrDefault(f => (f.Name == "_instance" || f.Name == "instance") && f.IsStatic);

            if (instProp != null && gns.Add("get_Instance"))
            {
                string rt = Utils.GetCppType(instProp.PropertyType, type, cw.Imports);
                cw.Line($"static {rt} get_Instance() {{");
                cw.Indent();
                cw.Line($"static BNM::Method<{rt}> method = GetClass().GetMethod(O(\"get_Instance\"));");
                cw.Line("return method.Call();");
                cw.Unindent();
                cw.Line("}");
            }

            if (instField != null && gns.Add("GetInstance"))
            {
                string ft = Utils.GetCppType(instField.FieldType, type, cw.Imports);
                cw.Line($"static {ft} GetInstance() {{");
                cw.Indent();
                cw.Line($"static BNM::Field<{ft}> field = GetClass().GetField(\"{instField.Name}\");");
                cw.Line("return field.Get();");
                cw.Unindent();
                cw.Line("}");
            }
        }

        private static void GenerateConstants(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            foreach (var f in type.Fields.Where(f => f.IsLiteral && f.Constant != null))
            {
                string t = Utils.GetCppType(f.FieldType, type, cw.Imports);
                if ((t.Contains("*") && f.FieldType.FullName != "System.String") || t.Contains("&") || t.Contains("<"))
                    continue;
                string val = f.Constant.ToString()?.ToLowerInvariant();
                if (string.IsNullOrEmpty(val) || !gns.Add(f.Name))
                    continue;

                if (f.FieldType.FullName == "System.String")
                {
                    cw.Line($"static constexpr const char* {f.Name} = \"{f.Constant}\";");
                    continue;
                }

                var res = f.FieldType.Resolve();
                if (f.FieldType.FullName == "System.Single" || f.FieldType.FullName == "System.Double")
                {
                    if (!val.Contains(".") && !val.Contains("e"))
                        val += ".0";
                    if (f.FieldType.FullName == "System.Single")
                        val += "f";
                }
                else if (res != null && res.IsEnum)
                {
                    val = $"({t}){val}";
                }
                cw.Line($"static constexpr {t} {f.Name} = {val};");
            }
        }

        private static void GeneratePropertyMethods(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            foreach (var p in type.Properties.OrderBy(p => p.Name))
            {
                if (p.Name.Contains("<") || p.Name.Contains("."))
                    continue;
                string t = Utils.GetCppType(p.PropertyType, type, cw.Imports);
                if (t == "void*" || t.Contains("$"))
                    continue;

                if (Config.MethodAccessorStyle == Config.MethodStyle.Accessor)
                {
                    string mn = Utils.FormatInvalidName(p.Name);
                    while (!gns.Add(mn))
                        mn += "_p";
                    cw.Line($"BNM::Property<{t}>* {mn}() {{");
                    cw.Indent();
                    cw.Line($"static BNM::Property<{t}> property = GetClass().GetProperty(O(\"{p.Name}\"));");
                    if (!type.IsValueType)
                        cw.Line("property.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                    cw.Line("return &property;");
                    cw.Unindent();
                    cw.Line("}");
                }
                else
                {
                    if (p.GetMethod != null)
                    {
                        string mn = Config.FormatGetterName(p.Name);
                        while (!gns.Add(mn))
                            mn += "_pg";
                        cw.Line($"{t} {mn}() {{");
                        cw.Indent();
                        cw.Line($"static BNM::Method<{t}> _m = GetClass().GetMethod(O(\"{Config.GetPropertyMethodName(p.Name, true)}\"));");
                        if (!type.IsValueType)
                            cw.Line("_m.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                        cw.Line("return _m.Call();");
                        cw.Unindent();
                        cw.Line("}");
                    }
                    if (p.SetMethod != null)
                    {
                        string mn = Config.FormatSetterName(p.Name);
                        while (!gns.Add(mn))
                            mn += "_ps";
                        cw.Line($"void {mn}({t} value) {{");
                        cw.Indent();
                        cw.Line($"static BNM::Method<void> _m = GetClass().GetMethod(O(\"{Config.GetPropertyMethodName(p.Name, false)}\"));");
                        if (!type.IsValueType)
                            cw.Line("_m.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                        cw.Line("_m.Call(value);");
                        cw.Unindent();
                        cw.Line("}");
                    }
                }
            }
        }

        private static void GenerateEventMethods(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            foreach (var e in type.Events.OrderBy(e => e.Name))
            {
                if (e.Name.Contains("<") || e.Name.Contains("."))
                    continue;
                string t = Utils.GetCppType(e.EventType, type, cw.Imports);
                if (t.Contains("$") || t == "void*")
                    continue;

                string an = $"add_{e.Name}";
                string rn = $"remove_{e.Name}";

                if (gns.Add(an))
                {
                    cw.Line($"void {an}({t} d) {{");
                    cw.Indent();
                    cw.Line($"static BNM::Method<void> _m = GetClass().GetMethod(O(\"add_{e.Name}\"));");
                    if (!type.IsValueType)
                        cw.Line("_m.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                    cw.Line("_m.Call(d);");
                    cw.Unindent();
                    cw.Line("}");
                }
                if (gns.Add(rn))
                {
                    cw.Line($"void {rn}({t} d) {{");
                    cw.Indent();
                    cw.Line($"static BNM::Method<void> _m = GetClass().GetMethod(O(\"remove_{e.Name}\"));");
                    if (!type.IsValueType)
                        cw.Line("_m.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                    cw.Line("_m.Call(d);");
                    cw.Unindent();
                    cw.Line("}");
                }
            }
        }

        private static void GenerateFieldGetter(FieldDefinition f, CodeWriter cw, TypeDefinition current, HashSet<string> gns)
        {
            string t = Utils.GetCppType(f.FieldType, current, cw.Imports);
            var resolved = f.FieldType.Resolve();
            if (resolved != null && Utils.ShouldAddDependency(resolved, current))
                cw.Imports.Add(resolved);
            if (t.Contains("$") || (f.FieldType.IsGenericParameter && !current.HasGenericParameters))
                return;

            if (Config.MethodAccessorStyle == Config.MethodStyle.Accessor)
            {
                string fn = Utils.FormatInvalidName(f.Name);
                while (!gns.Add(fn))
                    fn += "_f";
                cw.Line($"{(f.IsStatic ? "static " : "")}BNM::Field<{t}>* {fn}() {{");
                cw.Indent();
                cw.Line($"static BNM::Field<{t}> _field = GetClass().GetField(O(\"{f.Name}\"));");
                if (!f.IsStatic)
                    cw.Line("_field.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                cw.Line("return &_field;");
                cw.Unindent();
                cw.Line("}");
            }
            else
            {
                string mn = Config.FormatGetterName(f.Name);
                while (!gns.Add(mn))
                    mn += "_f";
                cw.Line($"{(f.IsStatic ? "static " : "")}{t} {mn}() {{");
                cw.Indent();
                cw.Line($"static BNM::Field<{t}> _field = GetClass().GetField(O(\"{f.Name}\"));");
                if (!f.IsStatic)
                    cw.Line("_field.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                cw.Line("return _field.Get();");
                cw.Unindent();
                cw.Line("}");
            }
        }

        private static void GenerateFieldSetter(FieldDefinition f, CodeWriter cw, TypeDefinition current, HashSet<string> gns)
        {
            if (f.IsInitOnly || Config.MethodAccessorStyle == Config.MethodStyle.Accessor)
                return;
            string t = Utils.GetCppType(f.FieldType, current, cw.Imports);
            var resolved = f.FieldType.Resolve();
            if (resolved != null && Utils.ShouldAddDependency(resolved, current))
                cw.Imports.Add(resolved);
            if (t.Contains("$") || (f.FieldType.IsGenericParameter && !current.HasGenericParameters))
                return;

            string mn = Config.FormatSetterName(f.Name);
            while (!gns.Add(mn))
                mn += "_fs";
            cw.Line($"{(f.IsStatic ? "static " : "")}void {mn}({t} value) {{");
            cw.Indent();
            cw.Line($"static BNM::Field<{t}> _field = GetClass().GetField(O(\"{f.Name}\"));");
            if (!f.IsStatic)
                cw.Line("_field.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
            cw.Line("_field.Set(value);");
            cw.Unindent();
            cw.Line("}");
        }

        private static void GenerateMethodDeclarations(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            var methodSigs = new HashSet<string>(StringComparer.Ordinal);
            var nonMethodNames = new HashSet<string>(gns, StringComparer.Ordinal);

            foreach (var m in type.Methods.Where(m => !m.IsConstructor && m.Name != ".cctor" && !m.Name.Contains("<") && !m.Name.Contains(".") && !OperatorNames.Contains(m.Name)).OrderBy(m => m.Name).ThenBy(m => m.Parameters.Count))
            {
                string mn = Utils.FormatInvalidName(m.Name);
                bool bad = false;
                var pts = new List<string>(m.Parameters.Count);
                foreach (var p in m.Parameters)
                {
                    string pt = Utils.GetCppType(p.ParameterType, type, cw.Imports);
                    if (pt.Contains("$") || (p.ParameterType.IsGenericParameter && !type.HasGenericParameters && !m.HasGenericParameters))
                    {
                        bad = true;
                        break;
                    }
                    pts.Add(pt);
                }
                if (bad)
                    continue;

                string rt = Utils.GetCppType(m.ReturnType, type, cw.Imports);
                if (rt.Contains("$") || (m.ReturnType.IsGenericParameter && !type.HasGenericParameters && !m.HasGenericParameters))
                    continue;

                var tmps = new List<string>();
                if (m.HasGenericParameters)
                    tmps.AddRange(m.GenericParameters.Select(p => $"typename {Utils.FormatInvalidName(p.Name)}"));

                for (int i = 0; i < pts.Count; i++)
                {
                    if (pts[i] == "void*")
                    {
                        tmps.Add($"typename TP{i} = void*");
                        pts[i] = $"TP{i}";
                    }
                }

                string sk = $"{mn}({string.Join(",", pts)})";
                string fmn = mn;
                if (nonMethodNames.Contains(fmn) || methodSigs.Contains(sk))
                {
                    int idx = 1;
                    while (nonMethodNames.Contains($"{mn}_{idx}") || methodSigs.Contains($"{mn}_{idx}({string.Join(",", pts)})"))
                        idx++;
                    fmn = $"{mn}_{idx}";
                }

                string typeName = Utils.FormatTypeNameForStruct(type);
                if (fmn == typeName)
                {
                    int idx = 1;
                    string b = $"{fmn}_m";
                    string c = b;
                    while (nonMethodNames.Contains(c) || methodSigs.Contains($"{c}({string.Join(",", pts)})"))
                        c = $"{b}{idx++}";
                    fmn = c;
                }

                methodSigs.Add(sk);
                gns.Add(fmn);

                if (tmps.Count > 0)
                    cw.Line($"template <{string.Join(", ", tmps)}>");

                var pns = Utils.MakeValidParams(m.Parameters.Select(p => p.Name).ToArray());
                string pl = string.Join(", ", pts.Zip(pns, (pt, pn) => $"{pt} {pn}"));

                if (Config.MethodAccessorStyle == Config.MethodStyle.Accessor)
                {
                    string pNames = m.Parameters.Count > 0 ? ", {" + string.Join(", ", m.Parameters.Select(p => $"\"{p.Name}\"")) + "}" : "";
                    cw.Line($"{(m.IsStatic ? "static " : "")}BNM::Method<{rt}>* {fmn}({pl}) {{");
                    cw.Indent();
                    cw.Line($"static BNM::Method<{rt}> _m = GetClass().GetMethod(O(\"{m.Name}\"){pNames});");
                    if (!m.IsStatic)
                        cw.Line("_m.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                    cw.Line("return &_m;");
                    cw.Unindent();
                    cw.Line("}");
                }
                else
                {
                    string pNames = m.Parameters.Count > 0 ? ", {" + string.Join(", ", m.Parameters.Select(p => $"\"{p.Name}\"")) + "}" : "";
                    string cp = m.Parameters.Count > 0 ? string.Join(", ", pns.Select((pn, i) => (m.Parameters[i].ParameterType.IsByReference ? "&" : "") + pn)) : "";
                    cw.Line($"{(m.IsStatic ? "static " : "")}{rt} {fmn}({pl}) {{");
                    cw.Indent();
                    cw.Line($"static BNM::Method<{rt}> _m = GetClass().GetMethod(O(\"{m.Name}\"){pNames});");
                    if (!m.IsStatic)
                        cw.Line("_m.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                    if (rt == "void")
                        cw.Line($"_m.Call({cp});");
                    else
                        cw.Line($"return _m.Call({cp});");
                    cw.Unindent();
                    cw.Line("}");
                }
            }

            if (!type.IsValueType && !type.IsInterface)
            {
                string n = Utils.FormatInvalidName(Utils.FormatTypeNameForStruct(type));
                if (gns.Add("NewArray"))
                    cw.Line($"static BNM::Structures::Mono::Array<{n}*>* NewArray(int size) {{ return GetClass().NewArray<{n}*>(size); }}");
                if (gns.Add("NewList"))
                    cw.Line($"static BNM::Structures::Mono::List<{n}*>* NewList() {{ return GetClass().NewList<{n}*>(); }}");
            }
        }

        private static void Ok(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(msg);
            Console.ResetColor();
        }

        private static void Warn(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"WARN: {msg}");
            Console.ResetColor();
        }

        private static void Err(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"ERR:  {msg}");
            Console.ResetColor();
        }
    }

    internal sealed class TypeDefComparer : IEqualityComparer<TypeDefinition>
    {
        public bool Equals(TypeDefinition x, TypeDefinition y) => x?.FullName == y?.FullName;

        public int GetHashCode(TypeDefinition obj) => obj?.FullName?.GetHashCode() ?? 0;
    }
}
