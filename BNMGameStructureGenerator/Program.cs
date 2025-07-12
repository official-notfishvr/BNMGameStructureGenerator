using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace BNMGameStructureGenerator
{
    internal class Program
    {
        public static HashSet<string> definedTypes = new HashSet<string>();

        static void Main(string[] args)
        {
            string dllPath = "./Files/Assembly-CSharp.dll";
            string outputDir = "BNMResolves";
            bool singleFileMode = args.Contains("--single-file") || args.Contains("-s");
            bool useReflection = args.Contains("--reflection") || args.Contains("-r");

            if (!Directory.Exists("./Files")) { Directory.CreateDirectory("Files"); }
            if (!Directory.Exists("./BNMResolves")) { Directory.CreateDirectory("BNMResolves"); }
            if (!File.Exists(dllPath))
            {
                Console.WriteLine($"Error: {dllPath} not found.");
                Console.WriteLine("Please make sure Assembly-CSharp.dll is in the ./Files folder.");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            try
            {
                List<object> allTypes = new List<object>();
                
                try
                {
                    Assembly assembly = Assembly.LoadFrom(dllPath);
                    List<Type> reflectionTypes = new List<Type>();
                    try
                    {
                        reflectionTypes.AddRange(assembly.GetTypes());
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        if (ex.Types != null) { reflectionTypes.AddRange(ex.Types.Where(t => t != null)); }
                    }
                    allTypes.AddRange(reflectionTypes.Cast<object>());
                    Console.WriteLine($"Loaded {reflectionTypes.Count} types using reflection");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not load types using reflection: {ex.Message}");
                }

                try
                {
                    ModuleDefMD module = ModuleDefMD.Load(dllPath);
                    var dnlibTypes = module.GetTypes().Cast<object>().ToList();
                    allTypes.AddRange(dnlibTypes);
                    Console.WriteLine($"Loaded {dnlibTypes.Count} types using dnlib");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not load types using dnlib: {ex.Message}");
                }

                var uniqueTypes = allTypes
                    .Where(t => t != null && (Utils.IsClassType(t) || Utils.IsEnum(t)) &&
                        !Utils.GetFullName(t).Contains("System.Collections") && 
                        !Utils.GetTypeName(t).Contains("IEnumerator") &&
                        !Utils.GetTypeName(t).Contains("IEnumerable") && 
                        !Utils.GetTypeName(t).Contains("ICollection") &&
                        !Utils.GetTypeName(t).Contains("IList") && 
                        !Utils.GetTypeName(t).Contains("IDictionary"))
                    .GroupBy(t => Utils.GetFullName(t))
                    .Select(g => g.First())
                    .ToList();

                Console.WriteLine($"Total unique types to process: {uniqueTypes.Count}");

                if (uniqueTypes.Count == 0)
                {
                    Console.WriteLine("No valid types found to process.");
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return;
                }

                ProcessCombinedTypes(uniqueTypes, singleFileMode, outputDir);
            }
            catch (Exception ex)
            {
                string errorMsg = $"Error processing assembly: {ex.Message}";
                Console.WriteLine(errorMsg);
                File.WriteAllText("GeneratorError.txt", $"{errorMsg}\n\nFull Exception:\n{ex}");
                Console.WriteLine("Error details saved to: GeneratorError.txt");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
        static void ProcessCombinedTypes(List<object> types, bool singleFileMode, string outputDir)
        {
            Console.WriteLine(singleFileMode ? "Running in single file mode..." : "Running in folder mode...");
            if (!singleFileMode && Directory.Exists(outputDir)) { Directory.Delete(outputDir, true); }
            if (!singleFileMode) { Directory.CreateDirectory(outputDir); }

            Console.WriteLine($"Successfully loaded: {types.Count} types");
            Console.WriteLine("Generating C++ headers...");
            Console.WriteLine("=" + new string('=', 50));
            Console.WriteLine();

            var classes = types.Where(t => t != null && (Utils.IsClassType(t) || Utils.IsEnum(t)) &&
                !Utils.GetFullName(t).Contains("System.Collections") && 
                !Utils.GetTypeName(t).Contains("IEnumerator") &&
                !Utils.GetTypeName(t).Contains("IEnumerable") && 
                !Utils.GetTypeName(t).Contains("ICollection") &&
                !Utils.GetTypeName(t).Contains("IList") && 
                !Utils.GetTypeName(t).Contains("IDictionary")).ToList();

            var safeClasses = classes.Where(t => Utils.SafeGetNamespace(t) != null).ToList();
            var groupedClasses = safeClasses.GroupBy(t => Utils.SafeGetNamespace(t)).OrderBy(g => g.Key);

            Console.WriteLine($"Found {classes.Count} classes and enums to process...");

            if (singleFileMode) { GenerateSingleFile(classes, groupedClasses.ToArray(), outputDir); }
            else { GenerateFolderStructure(classes, groupedClasses.ToArray(), outputDir); }
        }
        static void GenerateSingleFile<T>(List<T> classes, IGrouping<string, T>[] groupedClasses, string outputDir)
        {
            var output = new StringBuilder();
            output.AppendLine("#pragma once");
            output.AppendLine("using namespace BNM;");
            output.AppendLine("using namespace BNM::IL2CPP;");
            output.AppendLine("using namespace BNM::Structures;");
            output.AppendLine("using namespace BNM::Structures::Unity;");
            output.AppendLine("using namespace BNM::UnityEngine;");
            output.AppendLine("#define O(str) BNM_OBFUSCATE(str)");
            output.AppendLine("#include \"BNMResolve.hpp\"");
            output.AppendLine();

            foreach (var type in classes)
            {
                string typeName = Utils.GetTypeName(type);
                definedTypes.Add(Utils.CleanTypeName(typeName));
            }

            HashSet<string> generatedEnums = new HashSet<string>();
            HashSet<string> generatedTypes = new HashSet<string>();

            output.AppendLine("// Forward declarations");
            foreach (var namespaceGroup in groupedClasses)
            {
                string namespaceName = namespaceGroup.Key;
                
                if (namespaceName == "Global")
                {
                    output.AppendLine("// Global namespace types");
                    var sortedClasses = namespaceGroup.OrderBy(t => Utils.GetTypeName(t));
                    foreach (var type in sortedClasses)
                    {
                        string typeName = Utils.GetTypeName(type);
                        string typeNamespace = Utils.GetNamespace(type);
                        string cleanTypeName = Utils.CleanTypeName(typeName);
                        if (Utils.ReservedTypeNames.Contains(cleanTypeName)) { continue; }
                        if (generatedTypes.Contains(cleanTypeName)) { continue; }
                        generatedTypes.Add(cleanTypeName);
                        if (Utils.IsEnum(type))
                        {
                            string enumName = Utils.FormatTypeNameForStruct(typeName, typeNamespace);
                            if (generatedEnums.Contains(enumName)) { continue; }
                            generatedEnums.Add(enumName);
                            string underlyingType = Utils.GetEnumUnderlyingType(type);
                            output.AppendLine($"enum class {enumName} : {underlyingType};");
                        }
                        else
                        {
                            output.AppendLine($"struct {Utils.FormatTypeNameForStruct(typeName, typeNamespace)};");
                        }
                    }
                }
                else
                {
                    output.AppendLine($"namespace {namespaceName.Replace(".", "::")} {{");
                    var sortedClasses = namespaceGroup.OrderBy(t => Utils.GetTypeName(t));
                    foreach (var type in sortedClasses)
                    {
                        string typeName = Utils.GetTypeName(type);
                        string typeNamespace = Utils.GetNamespace(type);
                        string cleanTypeName = Utils.CleanTypeName(typeName);
                        if (Utils.ReservedTypeNames.Contains(cleanTypeName)) { continue; }
                        if (generatedTypes.Contains(cleanTypeName)) { continue; }
                        generatedTypes.Add(cleanTypeName);
                        if (Utils.IsEnum(type))
                        {
                            string enumName = Utils.FormatTypeNameForStruct(typeName, typeNamespace);
                            if (generatedEnums.Contains(enumName)) { continue; }
                            generatedEnums.Add(enumName);
                            string underlyingType = Utils.GetEnumUnderlyingType(type);
                            output.AppendLine($"    enum class {enumName} : {underlyingType};");
                        }
                        else
                        {
                            output.AppendLine($"    struct {Utils.FormatTypeNameForStruct(typeName, typeNamespace)};");
                        }
                    }
                    output.AppendLine("}");
                }
                output.AppendLine();
            }

            generatedEnums.Clear();
            generatedTypes.Clear();

            var allWarnings = new List<string>();

            foreach (var namespaceGroup in groupedClasses)
            {
                string namespaceName = namespaceGroup.Key;
                bool isGlobalNamespace = namespaceName == "Global";

                if (!isGlobalNamespace) { output.AppendLine($"namespace {namespaceName.Replace(".", "::")} {{"); }

                var sortedClasses = namespaceGroup.OrderBy(t => Utils.GetTypeName(t));

                foreach (var type in sortedClasses)
                {
                    string typeName = Utils.GetTypeName(type);
                    string typeNamespace = Utils.GetNamespace(type);
                    string cleanTypeName = Utils.CleanTypeName(typeName);
                    if (Utils.ReservedTypeNames.Contains(cleanTypeName)) { continue; }
                    if (generatedTypes.Contains(cleanTypeName)) { continue; }
                    generatedTypes.Add(cleanTypeName);
                    Console.WriteLine($"Processing: {Utils.GetFullName(type)}");

                    if (Utils.IsEnum(type))
                    {
                        string enumName = Utils.FormatTypeNameForStruct(typeName, typeNamespace);
                        if (generatedEnums.Contains(enumName)) { continue; }
                        generatedEnums.Add(enumName);
                        GenerateCppEnum(type, output, isGlobalNamespace);
                    }
                    else
                    {
                        GenerateCppClass(type, output, isGlobalNamespace);
                    }
                }

                if (!isGlobalNamespace) { output.AppendLine("}"); }
                output.AppendLine();
            }

            string outputPath = Path.Combine(outputDir, "BNMResolves.hpp");
            var finalOutput = output.ToString().Replace("StringComparison", "int");
            File.WriteAllText(outputPath, finalOutput);

            if (allWarnings.Count > 0)
            {
                string warnPath = Path.Combine(outputDir, "GenerationWarnings.txt");
                File.WriteAllLines(warnPath, allWarnings);
                Console.WriteLine($"All warnings saved to: {warnPath}");
            }

            Console.WriteLine();
            Console.WriteLine($"C++ headers saved to: {outputPath}");

            ValidateGeneratedCode(outputPath);
        }
        static void GenerateFolderStructure<T>(List<T> classes, IGrouping<string, T>[] groupedClasses, string outputDir)
        {
            HashSet<string> generatedTypes = new HashSet<string>();
            var allWarnings = new List<string>();
            
            foreach (var namespaceGroup in groupedClasses)
            {
                string namespaceName = namespaceGroup.Key;
                string namespaceDir;

                if (namespaceName == "Global") { namespaceDir = Path.Combine(outputDir, "Global"); }
                else
                {
                    string namespacePath = namespaceName.Replace(".", "/");
                    namespaceDir = Path.Combine(outputDir, namespacePath);
                }

                Directory.CreateDirectory(namespaceDir);

                var sortedClasses = namespaceGroup.OrderBy(t => Utils.GetTypeName(t));

                HashSet<string> generatedEnums = new HashSet<string>();

                foreach (var type in sortedClasses)
                {
                    string typeName = Utils.GetTypeName(type);
                    string typeNamespace = Utils.GetNamespace(type);
                    string cleanTypeName = Utils.CleanTypeName(typeName);
                    if (Utils.ReservedTypeNames.Contains(cleanTypeName)) continue;
                    if (generatedTypes.Contains(cleanTypeName)) continue;
                    generatedTypes.Add(cleanTypeName);
                    Console.WriteLine($"Processing: {Utils.GetFullName(type)}");

                    var classContent = new StringBuilder();
                    classContent.AppendLine("#pragma once");
                    classContent.AppendLine("using namespace BNM;");
                    classContent.AppendLine("using namespace BNM::IL2CPP;");
                    classContent.AppendLine("using namespace BNM::Structures;");
                    classContent.AppendLine("using namespace BNM::Structures::Unity;");
                    classContent.AppendLine("using namespace BNM::UnityEngine;");
                    classContent.AppendLine("#define O(str) BNM_OBFUSCATE(str)");
                    classContent.AppendLine("#include \"BNMResolve.hpp\"");
                    classContent.AppendLine();

                    bool isGlobalNamespace = string.IsNullOrEmpty(typeNamespace) || typeNamespace == "Global";
                    string actualNamespaceDir = isGlobalNamespace ? Path.Combine(outputDir, "Global") : namespaceDir;
                    Directory.CreateDirectory(actualNamespaceDir);
                    
                    if (!isGlobalNamespace)
                    {
                        classContent.AppendLine($"namespace {typeNamespace.Replace(".", "::")} {{");
                        classContent.AppendLine();
                    }

                    var otherTypesInNamespace = sortedClasses.Where(t => !t.Equals(type)).ToList();
                    if (otherTypesInNamespace.Any())
                    {
                        string prefix = isGlobalNamespace ? "" : "    ";
                        classContent.AppendLine($"// {prefix}Forward declarations for other types in this namespace");
                        foreach (var otherType in otherTypesInNamespace)
                        {
                            string otherTypeName = Utils.GetTypeName(otherType);
                            string otherNamespaceName = Utils.GetNamespace(otherType);
                            string cleanOtherTypeName = Utils.CleanTypeName(otherTypeName);
                            if (Utils.ReservedTypeNames.Contains(cleanOtherTypeName)) continue;
                            if (generatedEnums.Contains(cleanOtherTypeName)) continue;
                            if (Utils.IsEnum(otherType))
                            {
                                string enumName = Utils.FormatTypeNameForStruct(otherTypeName, otherNamespaceName);
                                if (generatedEnums.Contains(enumName)) continue;
                                generatedEnums.Add(enumName);
                                string underlyingType = Utils.GetEnumUnderlyingType(otherType);
                                classContent.AppendLine($"{prefix}enum class {enumName} : {underlyingType};");
                            }
                            else
                            {
                                classContent.AppendLine($"{prefix}struct {Utils.FormatTypeNameForStruct(otherTypeName, otherNamespaceName)};");
                            }
                        }
                        classContent.AppendLine();
                    }

                    if (Utils.IsEnum(type))
                    {
                        string enumName = Utils.FormatTypeNameForStruct(typeName, typeNamespace);
                        if (generatedEnums.Contains(enumName)) continue;
                        generatedEnums.Add(enumName);
                        GenerateCppEnum(type, classContent, isGlobalNamespace);
                    }
                    else
                    {
                        GenerateCppClass(type, classContent, isGlobalNamespace);
                    }

                    if (!isGlobalNamespace) { classContent.AppendLine("}"); }

                    string className = Utils.FormatTypeNameForStruct(typeName, typeNamespace);
                    string classFilePath = Path.Combine(actualNamespaceDir, $"{className}.hpp");
                    var finalClassContent = classContent.ToString().Replace("StringComparison", "int");
                    File.WriteAllText(classFilePath, finalClassContent);
                }
            }
            
            if (allWarnings.Count > 0)
            {
                string warnPath = Path.Combine(outputDir, "GenerationWarnings.txt");
                File.WriteAllLines(warnPath, allWarnings);
                Console.WriteLine($"All warnings saved to: {warnPath}");
            }
            Console.WriteLine();
            Console.WriteLine($"C++ headers saved to: {outputDir}/");

            ValidateGeneratedCode(outputDir);
        }
        static void GenerateCppClass<T>(T type, StringBuilder output, bool isGlobalNamespace = false, List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                GenerateCppClassGeneric(type, output, isGlobalNamespace, warnings);
            }
            catch (Exception ex)
            {
                string indent = isGlobalNamespace ? "" : "    ";
                string warn = $"// Error generating class {Utils.GetTypeName(type)}: {ex.Message}";
                output.AppendLine($"{indent}{warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static void GenerateCppClassGeneric<T>(T type, StringBuilder output, bool isGlobalNamespace = false, List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();

            string baseClass = Utils.GetBaseClass(type, type);
            string typeName = Utils.GetTypeName(type);
            string namespaceName = Utils.GetNamespace(type);
            string className = Utils.FormatTypeNameForStruct(typeName, namespaceName);
            string indent = isGlobalNamespace ? "" : "    ";

            string fullName = Utils.GetFullName(type);
            if (fullName != null && (fullName.Contains("System.Collections") || 
                Utils.GetTypeName(type).Contains("IEnumerator") || Utils.GetTypeName(type).Contains("IEnumerable") || 
                Utils.GetTypeName(type).Contains("ICollection") || Utils.GetTypeName(type).Contains("IList") || 
                Utils.GetTypeName(type).Contains("IDictionary")))
            {
                string warn = $"// SKIPPED: Class '{Utils.GetTypeName(type)}' is a System.Collections class";
                output.AppendLine($"{indent}{warn}");
                output.AppendLine();
                warnings.Add(warn);
                return;
            }

            if (baseClass.Contains("__REMOVE__"))
            {
                string warn = $"// REMOVED: Class '{className}' inherits from removed base class";
                output.AppendLine($"{indent}{warn}");
                output.AppendLine();
                warnings.Add(warn);
                return;
            }

            if (baseClass.Contains("__SELF_REF__"))
            {
                string warn = $"// REMOVED: Class '{className}' inherits from its own class type";
                output.AppendLine($"{indent}{warn}");
                output.AppendLine($"{indent}struct {className} : Behaviour {{");
                warnings.Add(warn);
            }
            else if (Utils.GetBaseType(type) != null && Utils.GetBaseTypeFullName(type) != "System.String" && Utils.GetBaseTypeNamespace(type) != null && !Utils.GetBaseTypeNamespace(type).StartsWith("System") && !Utils.GetBaseTypeNamespace(type).StartsWith("UnityEngine") && !(Utils.GetBaseType(type)?.Equals(type) ?? false))
            {
                string warn = $"// NOTE: Class '{className}' inherits from other class type '{Utils.CleanTypeName(Utils.GetBaseTypeName(type))}'";
                output.AppendLine($"{indent}{warn}");
                output.AppendLine($"{indent}struct {className}{baseClass} {{");
                warnings.Add(warn);
            }
            else if (!string.IsNullOrEmpty(baseClass))
            {
                output.AppendLine($"{indent}struct {className}{baseClass} {{");
            }
            else
            {
                output.AppendLine($"{indent}struct {className} : Behaviour {{");
            }
            output.AppendLine($"{indent}public:");

            var generatedNames = new HashSet<string>();

            output.AppendLine($"{indent}    static Class GetClass() {{");
            output.AppendLine($"{indent}        const char* className = \"{Utils.GetTypeName(type)}\";");
            
            string namespaceStr = Utils.GetNamespace(type) ?? "";
            output.AppendLine($"{indent}        static BNM::Class clahh = Class(O(\"{namespaceStr}\"), O(className), Image(O(\"Assembly-CSharp.dll\")));");
            output.AppendLine($"{indent}        return clahh;");
            output.AppendLine($"{indent}    }}");
            output.AppendLine();

            output.AppendLine($"{indent}    static MonoType* GetType() {{");
            output.AppendLine($"{indent}        return GetClass().GetMonoType();");
            output.AppendLine($"{indent}    }}");
            output.AppendLine();

            generatedNames.Add("GetClass");
            generatedNames.Add("GetType");
            generatedNames.Add("ToString");
            generatedNames.Add("Equals");
            generatedNames.Add("GetHashCode");
            generatedNames.Add("MemberwiseClone");
            generatedNames.Add("Finalize");

            var fields = Utils.GetFields(type).Where(f => !Utils.IsLiteral(f) && !Utils.GetFieldName(f).Contains("<")).ToArray();

            GenerateSingletonMethods(type, output, generatedNames, indent);

            foreach (var field in fields.OrderBy(f => Utils.GetFieldName(f)))
            {
                string getterName = $"Get{Utils.ToPascalCase(Utils.FormatInvalidName(Utils.GetFieldName(field)))}";
                if (!generatedNames.Add(getterName)) continue;
                GenerateFieldGetterGeneric(field, output, type, indent, warnings);
            }

            foreach (var field in fields.OrderBy(f => Utils.GetFieldName(f)))
            {
                string setterName = $"Set{Utils.ToPascalCase(Utils.FormatInvalidName(Utils.GetFieldName(field)))}";
                if (!generatedNames.Add(setterName)) continue;
                GenerateFieldSetterGeneric(field, output, type, indent, warnings);
            }

            GeneratePropertyMethods(type, output, generatedNames, indent, warnings);
            GenerateMethodDeclarations(type, output, generatedNames, indent, warnings);

            output.AppendLine($"{indent}}};");
            output.AppendLine();
        }
        static void GenerateCppEnum<T>(T type, StringBuilder output, bool isGlobalNamespace = false, List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                GenerateCppEnumGeneric(type, output, isGlobalNamespace, warnings);
            }
            catch (Exception ex)
            {
                string indent = isGlobalNamespace ? "" : "    ";
                string warn = $"// Error generating enum {Utils.GetTypeName(type)}: {ex.Message}";
                output.AppendLine($"{indent}{warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static void GenerateCppEnumGeneric<T>(T type, StringBuilder output, bool isGlobalNamespace = false, List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            
            string typeName = Utils.GetTypeName(type);
            string namespaceName = Utils.GetNamespace(type);
            string enumName = Utils.FormatTypeNameForStruct(typeName, namespaceName);
            string cppUnderlyingType = Utils.GetEnumUnderlyingType(type);
            string indent = isGlobalNamespace ? "" : "    ";

            output.AppendLine($"{indent}enum class {enumName} : {cppUnderlyingType} {{");

            var enumValues = Utils.GetEnumValues(type);
            var enumNames = Utils.GetEnumNames(type);
            var usedNames = new HashSet<string>();

            for (int i = 0; i < enumValues.Length; i++)
            {
                string valueName = enumNames[i];
                object value = enumValues.GetValue(i);

                string uniqueValueName = valueName;
                int suffix = 1;
                while (usedNames.Contains(uniqueValueName))
                {
                    uniqueValueName = $"{valueName}_{suffix}";
                    suffix++;
                }
                usedNames.Add(uniqueValueName);

                string valueString;
                try {
                    valueString = Convert.ToInt64(value).ToString();
                } catch {
                    valueString = value.ToString();
                }

                string formattedValueName = Utils.FormatInvalidName(uniqueValueName);
                if (formattedValueName.ToLower() == "delete")
                {
                    string warn = $"// Enum value '{formattedValueName}' = {valueString} removed because it gives error";
                    output.AppendLine($"{indent}    {warn}");
                    warnings.Add(warn);
                }
                else
                {
                    output.AppendLine($"{indent}    {formattedValueName} = {valueString},");
                }
            }

            output.AppendLine($"{indent}}};");
            output.AppendLine();
        }
        static void GenerateSingletonMethods<T>(T type, StringBuilder output, HashSet<string> generatedNames, string indent)
        {
            try
            {
                if (type is Type t)
                {
                    var instanceField = t.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                    var instanceProperty = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

                    if (instanceProperty != null)
                    {
                        string methodName = "get_Instance";
                        if (!generatedNames.Add(methodName)) return;
                        string className = Utils.FormatTypeNameForStruct(t.Name, t.Namespace);
                        output.AppendLine($"{indent}    static {className}* {methodName}() {{");
                        output.AppendLine($"{indent}        static Method<{className}*> method = GetClass().GetMethod(O(\"get_Instance\"));");
                        output.AppendLine($"{indent}        return method();");
                        output.AppendLine($"{indent}    }}");
                        output.AppendLine();
                    }

                    if (instanceField != null)
                    {
                        string methodName = "GetInstance";
                        if (!generatedNames.Add(methodName)) return;
                        string className = Utils.FormatTypeNameForStruct(t.Name, t.Namespace);
                        output.AppendLine($"{indent}    static {className}* {methodName}() {{");
                        output.AppendLine($"{indent}        static Field<{className}*> field = GetClass().GetField(O(\"_instance\"));");
                        output.AppendLine($"{indent}        return field();");
                        output.AppendLine($"{indent}    }}");
                        output.AppendLine();
                    }
                }
                else if (type is TypeDef td)
                {
                    var instanceField = td.Fields.FirstOrDefault(f => f.Name == "_instance" && f.IsStatic);
                    var instanceProperty = td.Properties.FirstOrDefault(p => p.Name == "Instance");

                    if (instanceProperty != null)
                    {
                        string methodName = "get_Instance";
                        if (!generatedNames.Add(methodName)) return;
                        string className = Utils.FormatTypeNameForStruct(td.Name, td.Namespace?.ToString());
                        output.AppendLine($"{indent}    static {className}* {methodName}() {{");
                        output.AppendLine($"{indent}        static Method<{className}*> method = GetClass().GetMethod(O(\"get_Instance\"));");
                        output.AppendLine($"{indent}        return method();");
                        output.AppendLine($"{indent}    }}");
                        output.AppendLine();
                    }

                    if (instanceField != null)
                    {
                        string methodName = "GetInstance";
                        if (!generatedNames.Add(methodName)) return;
                        string className = Utils.FormatTypeNameForStruct(td.Name, td.Namespace?.ToString());
                        output.AppendLine($"{indent}    static {className}* {methodName}() {{");
                        output.AppendLine($"{indent}        static Field<{className}*> field = GetClass().GetField(O(\"_instance\"));");
                        output.AppendLine($"{indent}        return field();");
                        output.AppendLine($"{indent}    }}");
                        output.AppendLine();
                    }
                }
            }
            catch
            {
            }
        }
        static void GenerateFieldGetterGeneric<TField, TClass>(TField field, StringBuilder output, TClass currentClass = default(TClass), string indent = "    ", List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                object fieldType = Utils.GetFieldType(field);
                if (Utils.IsNullableType(fieldType)) { fieldType = Utils.GetNullableUnderlyingType(fieldType); }
                
                string fieldTypeName = Utils.GetTypeName(fieldType);
                if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(fieldTypeName)))
                {
                    string warn = $"// REMOVED: Field '{Utils.GetFieldName(field)}' uses reserved type {fieldTypeName}";
                    output.AppendLine($"{indent}    {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }
                
                string cppType = Utils.GetCppType(fieldType, currentClass);
                if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__"))
                {
                    string warn = $"// {fieldTypeName} is not setup, removed";
                    output.AppendLine($"{indent}    {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }

                if (Utils.IsClassType(fieldType) && !Utils.IsStringType(fieldType) && Utils.GetTypeNamespace(fieldType) != null && !Utils.GetTypeNamespace(fieldType).StartsWith("System") && !Utils.GetTypeNamespace(fieldType).StartsWith("UnityEngine") && !fieldType.Equals(currentClass))
                {
                    string warn = $"// REMOVED: Field '{Utils.GetFieldName(field)}' uses other class type {Utils.CleanTypeName(fieldTypeName)}";
                    output.AppendLine($"{indent}    {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }

                string methodName = $"Get{Utils.ToPascalCase(Utils.FormatInvalidName(Utils.GetFieldName(field)))}";
                string fieldName = Utils.GetFieldName(field);
                string fieldVarName = Utils.ToCamelCase(Utils.FormatInvalidName(Utils.GetFieldName(field)));
                fieldVarName = Utils.FormatInvalidName(fieldVarName);

                output.AppendLine($"{indent}    {Utils.GetCppType(fieldType, currentClass)} {methodName}() {{");
                output.AppendLine($"{indent}        static Field<{Utils.GetCppType(fieldType, currentClass)}> {fieldVarName} = GetClass().GetField(O(\"{fieldName}\"));");

                if (!Utils.IsStatic(field)) { output.AppendLine($"{indent}        {fieldVarName}.SetInstance(this);"); }

                output.AppendLine($"{indent}        return {fieldVarName}();");
                output.AppendLine($"{indent}    }}");
                output.AppendLine();
            }
            catch (Exception ex)
            {
                string warn = $"// Error generating getter for {Utils.GetFieldName(field)}: {ex.Message}";
                output.AppendLine($"{indent}    {warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static void GenerateFieldSetterGeneric<TField, TClass>(TField field, StringBuilder output, TClass currentClass = default(TClass), string indent = "    ", List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                if (Utils.IsInitOnly(field)) return;
                
                object fieldType = Utils.GetFieldType(field);
                if (Utils.IsNullableType(fieldType)) { fieldType = Utils.GetNullableUnderlyingType(fieldType); }
                
                string fieldTypeName = Utils.GetTypeName(fieldType);
                if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(fieldTypeName)))
                {
                    string warn = $"// REMOVED: Field '{Utils.GetFieldName(field)}' uses reserved type {fieldTypeName}";
                    output.AppendLine($"{indent}    {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }
                
                string cppType = Utils.GetCppType(fieldType, currentClass);
                if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__"))
                {
                    string warn = $"// {fieldTypeName} is not setup, removed";
                    output.AppendLine($"{indent}    {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }

                if (Utils.IsClassType(fieldType) && !Utils.IsStringType(fieldType) && Utils.GetTypeNamespace(fieldType) != null &&  !Utils.GetTypeNamespace(fieldType).StartsWith("System") && !Utils.GetTypeNamespace(fieldType).StartsWith("UnityEngine") && !fieldType.Equals(currentClass))
                {
                    string warn = $"// REMOVED: Field '{Utils.GetFieldName(field)}' uses other class type {Utils.CleanTypeName(fieldTypeName)}";
                    output.AppendLine($"{indent}    {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }

                string methodName = $"Set{Utils.ToPascalCase(Utils.FormatInvalidName(Utils.GetFieldName(field)))}";
                string fieldName = Utils.GetFieldName(field);
                string fieldVarName = Utils.ToCamelCase(Utils.FormatInvalidName(Utils.GetFieldName(field)));
                fieldVarName = Utils.FormatInvalidName(fieldVarName);

                output.AppendLine($"{indent}    void {methodName}({Utils.GetCppType(fieldType, currentClass)} value) {{");
                output.AppendLine($"{indent}        static Field<{Utils.GetCppType(fieldType, currentClass)}> {fieldVarName} = GetClass().GetField(O(\"{fieldName}\"));");

                if (!Utils.IsStatic(field)) { output.AppendLine($"{indent}        {fieldVarName}.SetInstance(this);"); }

                output.AppendLine($"{indent}        {fieldVarName} = value;");
                output.AppendLine($"{indent}    }}");
                output.AppendLine();
            }
            catch (Exception ex)
            {
                string warn = $"// Error generating setter for {Utils.GetFieldName(field)}: {ex.Message}";
                output.AppendLine($"{indent}    {warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static void GeneratePropertyMethods<T>(T type, StringBuilder output, HashSet<string> generatedNames, string indent, List<string> warnings = null)
        {
            try
            {
                if (type is Type t)
                {
                    var properties = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    foreach (var prop in properties.OrderBy(p => p.Name))
                    {
                        if (prop.Name.Contains("<")) continue;
                        
                        if (prop.Name.Contains(".")) continue;
                        
                        string propName = Utils.FormatInvalidName(prop.Name);
                        if (Utils.ReservedTypeNames.Contains(propName)) continue;
                        
                        if (prop.CanRead)
                        {
                            string getterName = $"get_{prop.Name}";
                            if (!generatedNames.Add(getterName)) continue;
                            
                            Type propType = prop.PropertyType;
                            if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Nullable<>)) { propType = Nullable.GetUnderlyingType(propType); }
                            
                            string cppType = Utils.GetCppType(propType, t);
                            if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__")) continue;
                            
                            output.AppendLine($"{indent}    {cppType} {getterName}() {{");
                            output.AppendLine($"{indent}        static Method<{cppType}> method = GetClass().GetMethod(O(\"{getterName}\"));");
                            output.AppendLine($"{indent}        return method();");
                            output.AppendLine($"{indent}    }}");
                            output.AppendLine();
                        }
                        
                        if (prop.CanWrite)
                        {
                            string setterName = $"set_{prop.Name}";
                            if (!generatedNames.Add(setterName)) continue;
                            
                            Type propType = prop.PropertyType;
                            if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(Nullable<>)) { propType = Nullable.GetUnderlyingType(propType); }
                            
                            string cppType = Utils.GetCppType(propType, t);
                            if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__")) continue;
                            
                            output.AppendLine($"{indent}    void {setterName}({cppType} value) {{");
                            output.AppendLine($"{indent}        static Method<void> method = GetClass().GetMethod(O(\"{setterName}\"));");
                            output.AppendLine($"{indent}        method(value);");
                            output.AppendLine($"{indent}    }}");
                            output.AppendLine();
                        }
                    }
                }
                else if (type is TypeDef td)
                {
                    foreach (var prop in td.Properties.OrderBy(p => p.Name))
                    {
                        if (prop.Name.Contains("<")) continue;
                        
                        if (prop.Name.Contains(".")) continue;
                        
                        string propName = Utils.FormatInvalidName(prop.Name);
                        if (Utils.ReservedTypeNames.Contains(propName)) continue;
                        
                        if (prop.GetMethod != null)
                        {
                            string getterName = $"get_{prop.Name}";
                            if (!generatedNames.Add(getterName)) continue;
                            
                            TypeSig propType = prop.PropertySig.RetType;
                            if (propType is GenericInstSig genericSig && propType.FullName.Contains("System.Nullable")) { propType = genericSig.GenericArguments[0]; }
                            
                            string cppType = Utils.GetCppType(propType, td);
                            if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__")) continue;
                            
                            output.AppendLine($"{indent}    {cppType} {getterName}() {{");
                            output.AppendLine($"{indent}        static Method<{cppType}> method = GetClass().GetMethod(O(\"{getterName}\"));");
                            output.AppendLine($"{indent}        return method();");
                            output.AppendLine($"{indent}    }}");
                            output.AppendLine();
                        }
                        
                        if (prop.SetMethod != null)
                        {
                            string setterName = $"set_{prop.Name}";
                            if (!generatedNames.Add(setterName)) continue;
                            
                            TypeSig propType = prop.PropertySig.RetType;
                            if (propType is GenericInstSig genericSig && propType.FullName.Contains("System.Nullable")) { propType = genericSig.GenericArguments[0]; }
                            
                            string cppType = Utils.GetCppType(propType, td);
                            if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__")) continue;
                            
                            output.AppendLine($"{indent}    void {setterName}({cppType} value) {{");
                            output.AppendLine($"{indent}        static Method<void> method = GetClass().GetMethod(O(\"{setterName}\"));");
                            output.AppendLine($"{indent}        method(value);");
                            output.AppendLine($"{indent}    }}");
                            output.AppendLine();
                        }
                    }
                }
            }
            catch
            {
            }
        }
        static void GenerateMethodDeclarations<T>(T type, StringBuilder output, HashSet<string> generatedNames, string indent, List<string> warnings = null)
        {
            try
            {
                if (type is Type t)
                {
                    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Where(m => !m.IsSpecialName && !m.Name.Contains("<")).ToArray();
                    
                    foreach (var method in methods.OrderBy(m => m.Name))
                    {
                        if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(method.Name))) continue;
                        
                        if (method.Name.Contains(".")) continue;
                        
                        string methodName = Utils.FormatInvalidName(method.Name);
                        if (methodName.Contains("."))
                        {
                            methodName = methodName.Replace(".", "_");
                        }
                        if (!generatedNames.Add(methodName)) continue;
                        
                        Type returnType = method.ReturnType;
                        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Nullable<>))
                            returnType = Nullable.GetUnderlyingType(returnType);
                        string returnCppType = Utils.GetCppType(returnType, t);
                        if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(returnType.Name))) continue;
                        if (returnCppType.Contains("__REMOVE__") || returnCppType.Contains("__SELF_REF__")) continue;
                        var parameters = method.GetParameters();
                        var paramNames = Utils.MakeValidParams(parameters.Select(p => p.Name).ToArray());
                        var paramTypes = parameters.Select(p => {
                            Type paramType = p.ParameterType;
                            if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(Nullable<>)) { paramType = Nullable.GetUnderlyingType(paramType); }
                            if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(paramType.Name))) return "__REMOVE__";
                            return Utils.GetCppType(paramType, t);
                        }).ToArray();
                        bool hasInvalidParams = paramTypes.Any(pt => pt.Contains("__REMOVE__") || pt.Contains("__SELF_REF__"));
                        bool hasMethodParam = paramTypes.Any(pt => pt == "Method");
                        if (hasInvalidParams || hasMethodParam) continue;
                        
                        string paramList = string.Join(", ", paramTypes.Zip(paramNames, (paramType, name) => $"{paramType} {name}"));
                        
                        output.AppendLine($"{indent}    {returnCppType} {methodName}({paramList}) {{");
                        output.AppendLine($"{indent}        static Method<{returnCppType}> method = GetClass().GetMethod(O(\"{method.Name}\"));");
                        output.AppendLine($"{indent}        return method({string.Join(", ", paramNames)});");
                        output.AppendLine($"{indent}    }}");
                        output.AppendLine();
                    }
                }
                else if (type is TypeDef td)
                {
                    var methods = td.Methods.Where(m => !m.IsConstructor && !m.IsStaticConstructor && !m.Name.Contains("<")).ToArray();
                    
                    foreach (var method in methods.OrderBy(m => m.Name))
                    {
                        if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(method.Name))) continue;
                        
                        if (method.Name.Contains(".")) continue;
                        
                        string methodName = Utils.FormatInvalidName(method.Name);
                        if (methodName.Contains("."))
                        {
                            methodName = methodName.Replace(".", "_");
                        }
                        if (!generatedNames.Add(methodName)) continue;
                        
                        TypeSig returnType = method.MethodSig.RetType;
                        if (returnType is GenericInstSig genericSig && returnType.FullName.Contains("System.Nullable")) { returnType = genericSig.GenericArguments[0]; }
                        string returnCppType = Utils.GetCppType(returnType, td);
                        if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(returnType.TypeName))) continue;
                        if (returnCppType.Contains("__REMOVE__") || returnCppType.Contains("__SELF_REF__")) continue;
                        var parameters = method.Parameters;
                        var paramNames = Utils.MakeValidParams(parameters.Select(p => p.Name).ToArray());
                        var paramTypes = parameters.Select(p => {
                            TypeSig paramType = p.Type;
                            if (paramType is GenericInstSig genericSig2 && paramType.FullName.Contains("System.Nullable")) { paramType = genericSig2.GenericArguments[0]; }
                            if (Utils.ReservedTypeNames.Contains(Utils.CleanTypeName(paramType.TypeName))) return "__REMOVE__";
                            return Utils.GetCppType(paramType, td);
                        }).ToArray();
                        bool hasInvalidParams = paramTypes.Any(pt => pt.Contains("__REMOVE__") || pt.Contains("__SELF_REF__"));
                        bool hasMethodParam = paramTypes.Any(pt => pt == "Method");
                        if (hasInvalidParams || hasMethodParam) continue;
                        
                        string paramList = string.Join(", ", paramTypes.Zip(paramNames, (paramType, name) => $"{paramType} {name}"));
                        
                        output.AppendLine($"{indent}    {returnCppType} {methodName}({paramList}) {{");
                        output.AppendLine($"{indent}        static Method<{returnCppType}> method = GetClass().GetMethod(O(\"{method.Name}\"));");
                        output.AppendLine($"{indent}        return method({string.Join(", ", paramNames)});");
                        output.AppendLine($"{indent}    }}");
                        output.AppendLine();
                    }
                }
            }
            catch
            {
            }
        }
        static void ValidateGeneratedCode(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string content = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        Console.WriteLine("Warning: Generated file is empty");
                    }
                }
                else if (Directory.Exists(path))
                {
                    var files = Directory.GetFiles(path, "*.hpp", SearchOption.AllDirectories);
                    if (files.Length == 0)
                    {
                        Console.WriteLine("Warning: No .hpp files generated");
                    }
                }
            }
            catch
            {
            }
        }
    }
}