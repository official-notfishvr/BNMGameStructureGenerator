using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace SDKGeneratorBNM
{
    internal class Program
    {
        public static HashSet<string> DefinedTypes = new HashSet<string>();
        private static List<string> DllPaths = new List<string>();
        private static List<string> DumpCsPaths = new List<string>();
        private const string OutputDir = "SDK";
        private const string OutputExtension = ".hpp";
        private static readonly string[] ExcludedTypePatterns = Array.Empty<string>();
        private static readonly string[] OperatorNames =
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

        static void Main(string[] args)
        {
            Console.WriteLine("====================================");
            Console.WriteLine("  SDK Generator for BNM");
            Console.WriteLine("====================================");
            Console.WriteLine();
            bool singleFileMode = args.Contains("--single-file") || args.Contains("-s");
            bool getterSetterMode = args.Contains("--getter-setter") || args.Contains("-g");
            bool accessorMode = args.Contains("--accessor") || args.Contains("-a");
            bool BNMResolveMode = args.Contains("--bnm-resolve") || args.Contains("-b");
            if (args.Contains("--help") || args.Contains("-h"))
            {
                PrintHelp();
                return;
            }
            if (getterSetterMode)
                Config.MethodNamingStyle = Config.NamingStyle.GetterSetter;
            if (accessorMode)
                Config.MethodAccessorStyle = Config.MethodStyle.Accessor;
            if (BNMResolveMode)
                Config.UseBNMResolve = true;
            var filesToAdd = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--file" && i + 1 < args.Length)
                {
                    i++;
                    while (i < args.Length && !args[i].StartsWith("-"))
                    {
                        filesToAdd.Add(args[i]);
                        i++;
                    }
                    i--;
                }
                else if (!args[i].StartsWith("-"))
                {
                    filesToAdd.Add(args[i]);
                }
            }
            if (filesToAdd.Count == 0) filesToAdd.Add("Assembly-CSharp.dll");

            foreach (var file in filesToAdd)
                TryAddDllPath(file);

            if (DllPaths.Count == 0 && DumpCsPaths.Count == 0)
            {
                LogError("No valid DLLs or dump.cs files found or specified");
                WaitForExit();
                return;
            }

            Directory.CreateDirectory("./Files");
            Directory.CreateDirectory($"./{OutputDir}");

            try
            {
                var types = LoadTypes();
                if (types.Count == 0)
                {
                    LogError("No valid types found in assembly");
                    WaitForExit();
                    return;
                }
                Console.WriteLine($"[INFO] Processing {types.Count} types from {DllPaths.Count} assemblies...");
                ProcessTypes(types, singleFileMode);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[SUCCESS] Generation completed!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                LogError($"Fatal: {ex.Message}");
                File.WriteAllText("GeneratorError.txt", ex.ToString());
            }
            WaitForExit();
        }

        private static bool TryAddDllPath(string path)
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
                    DumpCsPaths.Add(dump);
                    return true;
                }
                LogError($"Assembly-CSharp.dll or dump.cs not found in {path}");
                return false;
            }
            if (File.Exists(path) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                DllPaths.Add(path);
                return true;
            }
            if (File.Exists(path) && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                DumpCsPaths.Add(path);
                return true;
            }
            string filesPath = Path.Combine("./Files", path);
            if (File.Exists(filesPath) && filesPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                DllPaths.Add(filesPath);
                return true;
            }
            if (File.Exists(filesPath) && filesPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                DumpCsPaths.Add(filesPath);
                return true;
            }
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                filesPath = Path.Combine("./Files", path + ".dll");
                if (File.Exists(filesPath))
                {
                    DllPaths.Add(filesPath);
                    return true;
                }
                filesPath = Path.Combine("./Files", path + ".cs");
                if (File.Exists(filesPath))
                {
                    DumpCsPaths.Add(filesPath);
                    return true;
                }
            }
            LogError($"Invalid path or DLL not found: {path}");
            return false;
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage: SDKGeneratorBNM [file] [options]\n\nArguments:");
            Console.WriteLine("  file             Path to DLL or dump.cs file (e.g., Assembly-CSharp.dll, dump.cs)");
            Console.WriteLine("                       Can be a full path, filename in ./Files, or just the name\n");
            Console.WriteLine("Options:");
            Console.WriteLine("  -s, --single-file    Generate all types in a single file");
            Console.WriteLine("  -g, --getter-setter  Use getter_setter naming style");
            Console.WriteLine("  -a, --accessor       Use accessor style (field()->Get())");
            Console.WriteLine("  -b, --bnm-resolve    Use BNMResolve types\n");
        }

        private static List<TypeDefinition> LoadTypes()
        {
            var allTypes = new List<TypeDefinition>();
            foreach (var dllPath in DllPaths)
            {
                try
                {
                    var resolver = new DefaultAssemblyResolver();
                    string dir = Path.GetDirectoryName(Path.GetFullPath(dllPath));
                    if (!string.IsNullOrEmpty(dir))
                        resolver.AddSearchDirectory(dir);
                    foreach (var otherDll in DllPaths)
                    {
                        string otherDir = Path.GetDirectoryName(Path.GetFullPath(otherDll));
                        if (!string.IsNullOrEmpty(otherDir))
                            resolver.AddSearchDirectory(otherDir);
                    }
                    var module = ModuleDefinition.ReadModule(dllPath, new ReaderParameters { AssemblyResolver = resolver });
                    foreach (var type in module.Types)
                        AddTypesRecursive(type, allTypes);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading {dllPath}: {ex.Message}");
                }
            }
            foreach (var dumpPath in DumpCsPaths)
            {
                try
                {
                    var dumpTypes = DumpCsParser.ParseDump(dumpPath);
                    allTypes.AddRange(dumpTypes);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading {dumpPath}: {ex}");
                }
            }
            return allTypes.Where(t => t != null && IsValidType(t)).Distinct(new TypeDefComparer()).ToList();
        }

        private static void AddTypesRecursive(TypeDefinition type, List<TypeDefinition> list)
        {
            list.Add(type);
            foreach (var nt in type.NestedTypes)
                AddTypesRecursive(nt, list);
        }

        private static bool IsValidType(TypeDefinition type)
        {
            if (type.IsInterface)
                return false;
            string ns = Utils.GetNamespace(type);
            if (ns.StartsWith("System") || ns.StartsWith("Unity"))
                return false;
            if (ns.StartsWith("<PrivateImplementationDetails"))
                return false;
            return !ExcludedTypePatterns.Any(p => type.FullName.Contains(p) || type.Name.Contains(p));
        }

        private static void ProcessTypes(List<TypeDefinition> types, bool singleFileMode)
        {
            if (!singleFileMode && Directory.Exists(OutputDir))
                Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(OutputDir);
            foreach (var type in types)
                DefinedTypes.Add(type.FullName);
            var grouped = types.GroupBy(t => Utils.FixNamespace(Utils.GetNamespace(t))).OrderBy(g => g.Key).ToArray();
            if (singleFileMode)
                GenerateSingleFile(grouped);
            else
                GenerateMultipleFiles(grouped);
        }

        private static void GenerateSingleFile(IGrouping<string, TypeDefinition>[] grouped)
        {
            var cw = new CodeWriter();
            cw.Line();
            var ens = new HashSet<string>();
            var tps = new HashSet<string>();
            foreach (var group in grouped)
                WriteForwardDeclarations(cw, group, ens, tps);
            ens.Clear();
            tps.Clear();
            foreach (var group in grouped)
            {
                cw.StartNamespace(group.Key);
                foreach (var type in group.OrderBy(t => t.Name))
                {
                    if (tps.Contains(type.FullName))
                        continue;
                    tps.Add(type.FullName);
                    if (type.IsEnum)
                    {
                        string name = Utils.FormatTypeNameForStruct(type);
                        if (ens.Add(name))
                            GenerateEnum(type, cw);
                    }
                    else
                        GenerateClass(type, cw);
                }
                cw.EndNamespace();
                cw.Line();
            }
            cw.Save(Path.Combine(OutputDir, $"SDK{OutputExtension}"));
        }

        private static void GenerateMultipleFiles(IGrouping<string, TypeDefinition>[] grouped)
        {
            int count = 0;
            foreach (var group in grouped)
            {
                string dir = Path.Combine(OutputDir, Utils.FixNamespace(group.Key));
                foreach (var type in group.OrderBy(t => t.Name))
                {
                    string ns = Utils.GetNamespace(type);
                    var cw = new CodeWriter();
                    cw.StartNamespace(ns);
                    if (type.IsEnum)
                        GenerateEnum(type, cw);
                    else
                        GenerateClass(type, cw);
                    cw.EndNamespace();
                    string name = Utils.FormatTypeNameForStruct(type);
                    cw.Save(Path.Combine(dir, $"{name}{OutputExtension}"));
                    count++;
                }
            }
            GenerateForwardDeclarationsFile(grouped);
            Console.WriteLine($"[INFO] Generated {count} header files");
        }

        private static void GenerateForwardDeclarationsFile(IGrouping<string, TypeDefinition>[] grouped)
        {
            var cw = new CodeWriter();
            cw.Line();
            var ens = new HashSet<string>();
            var tps = new HashSet<string>();
            foreach (var group in grouped)
            {
                cw.StartNamespace(group.Key);
                foreach (var type in group.OrderBy(t => t.Name))
                {
                    if (!tps.Add(type.FullName))
                        continue;
                    string name = Utils.FormatTypeNameForStruct(type);
                    if (type.IsEnum)
                    {
                        if (ens.Add(name))
                            cw.Line($"enum class {name} : {Utils.GetEnumUnderlyingType(type)};");
                    }
                    else
                    {
                        if (type.HasGenericParameters)
                            cw.Line($"template <{string.Join(", ", type.GenericParameters.Select(p => $"typename {Utils.FormatInvalidName(p.Name)}"))}> struct {name};");
                        else
                            cw.Line($"struct {name};");
                    }
                }
                cw.EndNamespace();
                cw.Line();
            }
            cw.Line("namespace BNM::Structures::Mono {\n    template <typename ...Parameters> struct Func : public MulticastDelegate<void> {};\n}\n");
            cw.Line("namespace System {\n    typedef ::BNM::Structures::Mono::Action<> Action;\n    template <typename ...T> using ActionT = ::BNM::Structures::Mono::Action<T...>;\n    template <typename ...T> using Func = ::BNM::Structures::Mono::Func<T...>;\n}\n");
            cw.Save(Path.Combine(OutputDir, "ForwardDeclarations.hpp"));
        }

        private static void WriteForwardDeclarations(CodeWriter cw, IGrouping<string, TypeDefinition> group, HashSet<string> ens, HashSet<string> tps)
        {
            cw.StartNamespace(group.Key);
            foreach (var type in group.OrderBy(t => t.Name))
            {
                if (!tps.Add(type.FullName))
                    continue;
                string name = Utils.FormatTypeNameForStruct(type);
                if (type.IsEnum)
                {
                    if (ens.Add(name))
                        cw.Line($"enum class {name} : {Utils.GetEnumUnderlyingType(type)};");
                }
                else
                {
                    if (type.HasGenericParameters)
                        cw.Line($"template <{string.Join(", ", type.GenericParameters.Select(p => $"typename {Utils.FormatInvalidName(p.Name)}"))}> struct {name};");
                    else
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
                string name = Utils.FormatTypeNameForStruct(type);
                string bc = GetBaseClass(type, cw.Imports);
                var gns = new HashSet<string> { "GetClass", "GetType", "ToString", "Equals", "GetHashCode", "MemberwiseClone", "Finalize", "NewArray", "NewList" };
                if (type.HasGenericParameters)
                    cw.Line($"template <{string.Join(", ", type.GenericParameters.Select(p => $"typename {Utils.FormatInvalidName(p.Name)}"))}>");
                cw.Line($"struct {name}{(string.IsNullOrEmpty(bc) ? " : BNM::UnityEngine::MonoBehaviour" : bc)} {{");
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
                var fds = type.Fields.Where(f => !f.IsLiteral && !f.Name.Contains("<")).OrderBy(f => f.Name).ToArray();
                foreach (var f in fds)
                    GenerateFieldGetter(f, cw, type, gns);
                foreach (var f in fds)
                    GenerateFieldSetter(f, cw, type, gns);
                GenerateMethodDeclarations(type, cw, gns);
                //GenerateUtilityMethods(type, cw, gns);
                cw.Unindent();
                cw.Line("};");
            }
            catch { }
        }

        private static void GenerateConstants(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            foreach (var f in type.Fields.Where(f => f.IsLiteral && f.Constant != null))
            {
                string t = Utils.GetCppType(f.FieldType, type, cw.Imports);
                if (t.Contains("*") && f.FieldType.FullName != "System.String" || t.Contains("&") || t.Contains("<"))
                    continue;
                string val = f.Constant.ToString()?.ToLower();
                if (string.IsNullOrEmpty(val))
                    continue;
                if (f.FieldType.FullName == "System.String")
                {
                    if (gns.Add(f.Name))
                        cw.Line($"static constexpr const char* {f.Name} = \"{f.Constant}\";");
                    continue;
                }
                if (gns.Add(f.Name))
                {
                    var res = f.FieldType.Resolve();
                    if (f.FieldType.FullName == "System.Single" || f.FieldType.FullName == "System.Double")
                    {
                        if (!val.Contains(".") && !val.Contains("e"))
                            val += ".0";
                        if (f.FieldType.FullName == "System.Single")
                            val += "f";
                    }
                    else if (res != null && res.IsEnum)
                        val = $"({t}){val}";
                    cw.Line($"static constexpr {t} {f.Name} = {val};");
                }
            }
        }

        private static void GenerateEnum(TypeDefinition type, CodeWriter cw)
        {
            try
            {
                string name = Utils.FormatTypeNameForStruct(type);
                cw.Line($"enum class {name} : {Utils.GetEnumUnderlyingType(type)} {{");
                cw.Indent();
                var used = new HashSet<string>();
                foreach (var f in type.Fields.Where(f => f.IsStatic && f.IsLiteral))
                {
                    string fn = f.Name;
                    string un = fn;
                    int s = 1;
                    while (used.Contains(un))
                        un = $"{fn}_{s++}";
                    used.Add(un);
                    string val = f.Constant != null ? Convert.ToInt64(f.Constant).ToString() : "0";
                    string fmt = Utils.FormatInvalidName(un);
                    if (fmt.ToLower() != "delete")
                        cw.Line($"{fmt} = {val},");
                }
                cw.Unindent();
                cw.Line("};");
            }
            catch { }
        }

        private static void GenerateSingletonMethods(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            var p = type.Properties.FirstOrDefault(p => p.Name == "Instance" && p.GetMethod != null);
            var f = type.Fields.FirstOrDefault(f => (f.Name == "_instance" || f.Name == "instance") && f.IsStatic);
            string name = Utils.FormatTypeNameForStruct(type);

            if (p != null && gns.Add("get_Instance"))
            {
                string returnType = Utils.GetCppType(p.PropertyType, type, cw.Imports);
                cw.Line($"static {returnType} get_Instance() {{");
                cw.Indent();
                cw.Line($"static BNM::Method<{returnType}> method = GetClass().GetMethod(O(\"get_Instance\"));");
                cw.Line("return method.Call();");
                cw.Unindent();
                cw.Line("}");
            }

            if (f != null && gns.Add("GetInstance"))
            {
                string fieldType = Utils.GetCppType(f.FieldType, type, cw.Imports);
                cw.Line($"static {fieldType} GetInstance() {{");
                cw.Indent();
                cw.Line($"static BNM::Field<{fieldType}> field = GetClass().GetField(\"{f.Name}\");");
                cw.Line("return field.Get();");
                cw.Unindent();
                cw.Line("}");
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
            string mn = Config.FormatGetterName(f.Name);
            while (!gns.Add(mn))
                mn += "_f";
            if (Config.MethodAccessorStyle == Config.MethodStyle.Accessor)
            {
                cw.Line($"{(f.IsStatic ? "static " : "")}BNM::Field<{t}>* {f.Name}() {{");
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
            if (f.IsInitOnly)
                return;
            string t = Utils.GetCppType(f.FieldType, current, cw.Imports);
            var resolved = f.FieldType.Resolve();
            if (resolved != null && Utils.ShouldAddDependency(resolved, current))
                cw.Imports.Add(resolved);
            if (t.Contains("$") || (f.FieldType.IsGenericParameter && !current.HasGenericParameters))
                return;
            if (Config.MethodAccessorStyle == Config.MethodStyle.Accessor)
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

        private static void GeneratePropertyMethods(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            foreach (var p in type.Properties.OrderBy(prop => prop.Name))
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
                        cw.Line($"static BNM::Method<{t}> _method = GetClass().GetMethod(O(\"{Config.GetPropertyMethodName(p.Name, true)}\"));");
                        if (!type.IsValueType)
                            cw.Line("_method.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                        cw.Line("return _method.Call();");
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
                        cw.Line($"static BNM::Method<void> _method = GetClass().GetMethod(O(\"{Config.GetPropertyMethodName(p.Name, false)}\"));");
                        if (!type.IsValueType)
                            cw.Line("_method.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                        cw.Line("_method.Call(value);");
                        cw.Unindent();
                        cw.Line("}");
                    }
                }
            }
        }

        private static void GenerateEventMethods(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            foreach (var e in type.Events.OrderBy(evt => evt.Name))
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

        private static void GenerateMethodDeclarations(TypeDefinition type, CodeWriter cw, HashSet<string> gns)
        {
            var methodSigs = new HashSet<string>();
            var nonMethodNames = new HashSet<string>(gns);
            foreach (var m in type.Methods.Where(m => !m.IsConstructor && !m.Name.Contains("<") && !m.Name.Contains(".") && !OperatorNames.Contains(m.Name)).OrderBy(m => m.Name).ThenBy(m => m.Parameters.Count))
            {
                string mn = Utils.FormatInvalidName(m.Name).Replace(".", "_");
                bool prob = false;
                var pts = new List<string>();
                foreach (var p in m.Parameters)
                {
                    string pt = Utils.GetCppType(p.ParameterType, type, cw.Imports);
                    if (pt.Contains("$") || (p.ParameterType.IsGenericParameter && !type.HasGenericParameters && !m.HasGenericParameters))
                    {
                        prob = true;
                        break;
                    }
                    pts.Add(pt);
                }
                if (prob)
                    continue;
                string rt = Utils.GetCppType(m.ReturnType, type, cw.Imports);
                if (rt.Contains("$") || (m.ReturnType.IsGenericParameter && !type.HasGenericParameters && !m.HasGenericParameters))
                    continue;
                var tmps = new List<string>();
                if (m.HasGenericParameters)
                    tmps.AddRange(m.GenericParameters.Select(p => $"typename {Utils.FormatInvalidName(p.Name)}"));
                for (int i = 0; i < pts.Count; i++)
                    if (pts[i] == "void*")
                    {
                        tmps.Add($"typename TP{i} = void*");
                        pts[i] = $"TP{i}";
                    }
                string sk = $"{mn}({string.Join(",", pts)})";
                string fmn = mn;
                if (nonMethodNames.Contains(fmn) || methodSigs.Contains(sk))
                {
                    int i = 1;
                    while (nonMethodNames.Contains($"{mn}_{i}") || methodSigs.Contains($"{mn}_{i}({string.Join(",", pts)})"))
                        i++;
                    fmn = $"{mn}_{i}";
                }
                methodSigs.Add(sk);
                gns.Add(fmn);
                if (tmps.Count > 0)
                    cw.Line($"template <{string.Join(", ", tmps)}>");
                var pns = Utils.MakeValidParams(m.Parameters.Select(p => p.Name).ToArray());
                string pl = string.Join(", ", pts.Zip(pns, (pt, pn) => $"{pt} {pn}"));
                if (Config.MethodAccessorStyle == Config.MethodStyle.Accessor)
                {
                    string paramNames = m.Parameters.Count > 0 ? ", {" + string.Join(", ", m.Parameters.Select(p => $"\"{p.Name}\"")) + "}" : "";
                    cw.Line($"{(m.IsStatic ? "static " : "")}BNM::Method<{rt}>* {fmn}({pl}) {{");
                    cw.Indent();
                    cw.Line($"static BNM::Method<{rt}> _m = GetClass().GetMethod(O(\"{m.Name}\"){paramNames});");
                    if (!m.IsStatic)
                        cw.Line("_m.SetInstance(reinterpret_cast<::BNM::IL2CPP::Il2CppObject*>(this));");
                    cw.Line("return &_m;");
                    cw.Unindent();
                    cw.Line("}");
                }
                else
                {
                    cw.Line($"{(m.IsStatic ? "static " : "")}{rt} {fmn}({pl}) {{");
                    cw.Indent();
                    string cp = m.Parameters.Count > 0 ? string.Join(", ", pns.Select((p, i) => (m.Parameters[i].ParameterType.IsByReference ? "&" : "") + p)) : "";
                    string paramNames = m.Parameters.Count > 0 ? ", {" + string.Join(", ", m.Parameters.Select(p => $"\"{p.Name}\"")) + "}" : "";
                    cw.Line($"static BNM::Method<{rt}> _m = GetClass().GetMethod(O(\"{m.Name}\"){paramNames});");
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
        }

        private static void GenerateUtilityMethods(TypeDefinition type, CodeWriter cw, HashSet<string> gns) // why not
        {
            if (!type.IsValueType && !type.IsInterface)
            {
                string name = Utils.FormatTypeNameForStruct(type);
                if (gns.Add("NewArray"))
                    cw.Line($"static BNM::Structures::Mono::Array<{name}*>* NewArray(int size) {{ return GetClass().NewArray<{name}*>(size); }}");
                if (gns.Add("NewList"))
                    cw.Line($"static BNM::Structures::Mono::List<{name}*>* NewList() {{ return GetClass().NewList<{name}*>(); }}");
            }
        }

        private static string GetBaseClass(TypeDefinition type, HashSet<TypeDefinition> deps = null)
        {
            var bt = type.BaseType;
            if (bt == null || bt.FullName == "System.Object")
                return string.Empty;
            var res = bt.Resolve();
            if (res != null && Utils.ShouldAddDependency(res, type, true))
                deps.Add(res);
            string btn = Utils.CleanTypeName(bt.Name);
            if (bt.Namespace?.StartsWith("UnityEngine") ?? false)
                return btn switch
                {
                    "MonoBehaviour" or "Object" or "Component" => $" : BNM::UnityEngine::{btn}",
                    "Behaviour" => " : BNM::UnityEngine::MonoBehaviour",
                    _ => " : BNM::UnityEngine::Object",
                };
            if (res != null && Program.DefinedTypes.Contains(res.FullName))
            {
                string ns = Utils.FixNamespace(Utils.GetNamespace(res));
                string baseName = Utils.FormatTypeNameForStruct(res);
                if (bt is GenericInstanceType git)
                {
                    string args = string.Join(", ", git.GenericArguments.Select(a => Utils.GetCppType(a, type, deps)));
                    return $" : ::{ns}::{baseName}<{args}>";
                }
                return $" : ::{ns}::{baseName}";
            }
            return string.Empty;
        }

        private static void LogError(string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[ERROR] {msg}");
            Console.ResetColor();
        }

        private static void WaitForExit()
        {
            Console.WriteLine();
            try
            {
                if (!Console.IsInputRedirected)
                    Console.ReadKey();
            }
            catch
            {
            }
        }
    }

    internal class TypeDefComparer : IEqualityComparer<TypeDefinition>
    {
        public bool Equals(TypeDefinition x, TypeDefinition y) => x?.FullName == y?.FullName;

        public int GetHashCode(TypeDefinition obj) => obj?.FullName?.GetHashCode() ?? 0;
    }
}
