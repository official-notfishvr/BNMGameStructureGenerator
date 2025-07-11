using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;

namespace BNMGameStructureGenerator
{
    internal class Program
    {
        private static HashSet<string> definedTypes = new HashSet<string>();

        private static readonly HashSet<string> ReservedTypeNames = new HashSet<string>
        {
            // BNMResolve
            "RenderMode",

            // BNM
            "Method"
        };

        static void Main(string[] args)
        {
            string dllPath = "./Files/Assembly-CSharp.dll";
            string outputDir = "BNMResolves";
            bool singleFileMode = args.Contains("--single-file") || args.Contains("-s");
            if (!Directory.Exists("./Files"))
            {
                Directory.CreateDirectory("Files");
            }
            if (!Directory.Exists("./BNMResolves"))
            {
                Directory.CreateDirectory("BNMResolves");
            }
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
                Assembly assembly = Assembly.LoadFrom(dllPath);

                if (singleFileMode)
                {
                    Console.WriteLine("Running in single file mode...");
                }
                else
                {
                    Console.WriteLine("Running in folder mode...");
                    if (Directory.Exists(outputDir))
                    {
                        Directory.Delete(outputDir, true);
                    }
                    Directory.CreateDirectory(outputDir);
                }

                Console.WriteLine($"Successfully loaded: {assembly.GetName().Name}");
                Console.WriteLine($"Assembly Version: {assembly.GetName().Version}");
                Console.WriteLine("Generating C++ headers...");
                Console.WriteLine("=" + new string('=', 50));
                Console.WriteLine();

                Type[] types = null;
                List<Type> loadedTypes = new List<Type>();

                try
                {
                    types = assembly.GetTypes();
                    loadedTypes.AddRange(types);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    if (ex.Types != null)
                    {
                        loadedTypes.AddRange(ex.Types.Where(t => t != null));
                    }
                }

                var classes = loadedTypes.Where(t => t != null &&
                                               (t.IsClass || t.IsEnum) &&
                                               !t.IsAbstract &&
                                               !t.Name.Contains("<") &&
                                               !t.Name.StartsWith("_")).ToList();

                var safeClasses = classes.Where(t => SafeGetNamespace(t) != null).ToList();
                var groupedClasses = safeClasses.GroupBy(t => SafeGetNamespace(t)).OrderBy(g => g.Key);


                Console.WriteLine($"Found {classes.Count} classes and enums to process...");

                if (singleFileMode)
                {
                    GenerateSingleFile(classes, groupedClasses.ToArray(), outputDir);
                }
                else
                {
                    GenerateFolderStructure(classes, groupedClasses.ToArray(), outputDir);
                }
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
        static void GenerateSingleFile(List<Type> classes, IGrouping<string, Type>[] groupedClasses, string outputDir)
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
                definedTypes.Add(CleanTypeName(type.Name));
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
                    var sortedClasses = namespaceGroup.OrderBy(t => t.Name);
                    foreach (var type in sortedClasses)
                    {
                        string typeName = CleanTypeName(type.Name);
                        if (ReservedTypeNames.Contains(typeName)) { continue; }
                        if (generatedTypes.Contains(typeName)) { continue; }
                        generatedTypes.Add(typeName);
                        if (type.IsEnum)
                        {
                            string enumName = FormatInvalidName(typeName);
                            if (generatedEnums.Contains(enumName)) { continue; }
                            generatedEnums.Add(enumName);
                            string underlyingType = GetEnumUnderlyingType(type);
                            output.AppendLine($"enum class {enumName} : {underlyingType};");
                        }
                        else
                        {
                            output.AppendLine($"struct {FormatInvalidName(typeName)};");
                        }
                    }
                }
                else
                {
                    output.AppendLine($"namespace {namespaceName.Replace(".", "::")} {{");
                    var sortedClasses = namespaceGroup.OrderBy(t => t.Name);
                    foreach (var type in sortedClasses)
                    {
                        string typeName = CleanTypeName(type.Name);
                        if (ReservedTypeNames.Contains(typeName)) { continue; }
                        if (generatedTypes.Contains(typeName)) { continue; }
                        generatedTypes.Add(typeName);
                        if (type.IsEnum)
                        {
                            string enumName = FormatInvalidName(typeName);
                            if (generatedEnums.Contains(enumName)) { continue; }
                            generatedEnums.Add(enumName);
                            string underlyingType = GetEnumUnderlyingType(type);
                            output.AppendLine($"    enum class {enumName} : {underlyingType};");
                        }
                        else
                        {
                            output.AppendLine($"    struct {FormatInvalidName(typeName)};");
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
                
                if (!isGlobalNamespace)
                {
                    output.AppendLine($"namespace {namespaceName.Replace(".", "::")} {{");
                }

                var sortedClasses = namespaceGroup.OrderBy(t => t.Name);

                foreach (var type in sortedClasses)
                {
                    string typeName = CleanTypeName(type.Name);
                    if (ReservedTypeNames.Contains(typeName)) { continue; }
                    if (generatedTypes.Contains(typeName)) { continue; }
                    generatedTypes.Add(typeName);
                    Console.WriteLine($"Processing: {type.FullName ?? type.Name}");

                    if (type.IsEnum)
                    {
                        string enumName = FormatInvalidName(typeName);
                        if (generatedEnums.Contains(enumName)) { continue; }
                        generatedEnums.Add(enumName);
                        GenerateCppEnum(type, output, isGlobalNamespace);
                    }
                    else
                    {
                        GenerateCppClass(type, output, isGlobalNamespace);
                    }
                }

                if (!isGlobalNamespace)
                {
                    output.AppendLine("}");
                }
                output.AppendLine();
            }

            string outputPath = Path.Combine(outputDir, "BNMResolves.hpp");
            File.WriteAllText(outputPath, output.ToString());

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
        static void GenerateFolderStructure(List<Type> classes, IGrouping<string, Type>[] groupedClasses, string outputDir)
        {
            HashSet<string> generatedTypes = new HashSet<string>();
            var allWarnings = new List<string>();
            foreach (var namespaceGroup in groupedClasses)
            {
                string namespaceName = namespaceGroup.Key;
                string namespaceDir;
                
                if (namespaceName == "Global")
                {
                    namespaceDir = Path.Combine(outputDir, "Global");
                }
                else
                {
                    string namespacePath = namespaceName.Replace(".", "/");
                    namespaceDir = Path.Combine(outputDir, namespacePath);
                }

                Directory.CreateDirectory(namespaceDir);

                var sortedClasses = namespaceGroup.OrderBy(t => t.Name);

                foreach (var type in sortedClasses)
                {
                    string typeName = CleanTypeName(type.Name);
                    if (ReservedTypeNames.Contains(typeName)) { continue; }
                    if (generatedTypes.Contains(typeName)) { continue; }
                    generatedTypes.Add(typeName);
                }

                HashSet<string> generatedEnums = new HashSet<string>();

                foreach (var type in sortedClasses)
                {
                    string typeName = CleanTypeName(type.Name);
                    if (ReservedTypeNames.Contains(typeName)) { continue; }
                    if (generatedTypes.Contains(typeName)) { continue; }
                    generatedTypes.Add(typeName);
                    Console.WriteLine($"Processing: {type.FullName ?? type.Name}");

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

                    bool isGlobalNamespace = namespaceName == "Global";
                    
                    if (!isGlobalNamespace)
                    {
                        classContent.AppendLine($"namespace {namespaceName.Replace(".", "::")} {{");
                        classContent.AppendLine();
                    }

                    var otherTypesInNamespace = sortedClasses.Where(t => t != type).ToList();
                    if (otherTypesInNamespace.Any())
                    {
                        string prefix = isGlobalNamespace ? "" : "    ";
                        classContent.AppendLine($"{prefix}// Forward declarations for other types in this namespace");
                        foreach (var otherType in otherTypesInNamespace)
                        {
                            string otherTypeName = CleanTypeName(otherType.Name);
                            if (ReservedTypeNames.Contains(otherTypeName)) { continue; }
                            if (generatedEnums.Contains(otherTypeName)) { continue; }
                            if (otherType.IsEnum)
                            {
                                string enumName = FormatInvalidName(otherTypeName);
                                if (generatedEnums.Contains(enumName)) { continue; }
                                generatedEnums.Add(enumName);
                                string underlyingType = GetEnumUnderlyingType(otherType);
                                classContent.AppendLine($"{prefix}enum class {enumName} : {underlyingType};");
                            }
                            else
                            {
                                classContent.AppendLine($"{prefix}struct {FormatInvalidName(otherTypeName)};");
                            }
                        }
                        classContent.AppendLine();
                    }

                    if (type.IsEnum)
                    {
                        string enumName = FormatInvalidName(typeName);
                        if (generatedEnums.Contains(enumName)) { continue; }
                        generatedEnums.Add(enumName);
                        GenerateCppEnum(type, classContent, isGlobalNamespace);
                    }
                    else
                    {
                        GenerateCppClass(type, classContent, isGlobalNamespace);
                    }

                    if (!isGlobalNamespace)
                    {
                        classContent.AppendLine("}");
                    }

                    string className = FormatInvalidName(typeName);
                    string classFilePath = Path.Combine(namespaceDir, $"{className}.hpp");
                    File.WriteAllText(classFilePath, classContent.ToString());
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
        static void GenerateCppClass(Type type, StringBuilder output, bool isGlobalNamespace = false, List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                string baseClass = GetBaseClass(type, type);
                string className = FormatInvalidName(CleanTypeName(type.Name));
                string indent = isGlobalNamespace ? "" : "    ";

                if (baseClass.Contains("__REMOVE__"))
                {
                    string warn = $"REMOVED: Class '{className}' inherits from removed base class";
                    output.AppendLine($"{indent}// {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }

                if (baseClass.Contains("__SELF_REF__"))
                {
                    string warn = $"REMOVED: Class '{className}' inherits from its own class type";
                    output.AppendLine($"{indent}// {warn}");
                    output.AppendLine($"{indent}struct {className} : Behaviour {{");
                    warnings.Add(warn);
                }
                else if (type.BaseType != null && type.BaseType.IsClass && type.BaseType != typeof(string) && type.BaseType.Namespace != null && !type.BaseType.Namespace.StartsWith("System") && !type.BaseType.Namespace.StartsWith("UnityEngine") && type.BaseType != type)
                {
                    string warn = $"NOTE: Class '{className}' inherits from other class type '{CleanTypeName(type.BaseType.Name)}'";
                    output.AppendLine($"{indent}// {warn}");
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
                output.AppendLine($"{indent}        const char* className = \"{type.Name}\";");
                
                string namespaceStr = type.Namespace ?? "";
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

                BindingFlags fieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                var fields = type.GetFields(fieldFlags).Where(f => !f.IsLiteral && !f.Name.Contains("<")).ToArray();

                GenerateSingletonMethods(type, output, generatedNames, indent);

                foreach (var field in fields.OrderBy(f => f.Name))
                {
                    string getterName = $"Get{ToPascalCase(FormatInvalidName(field.Name))}";
                    if (!generatedNames.Add(getterName)) { continue; }
                    GenerateFieldGetter(field, output, type, indent, warnings);
                }

                foreach (var field in fields.OrderBy(f => f.Name))
                {
                    string setterName = $"Set{ToPascalCase(FormatInvalidName(field.Name))}";
                    if (!generatedNames.Add(setterName)) { continue; }
                    GenerateFieldSetter(field, output, type, indent, warnings);
                }

                GeneratePropertyMethods(type, output, generatedNames, indent, warnings);
                GenerateMethodDeclarations(type, output, generatedNames, indent, warnings);

                output.AppendLine($"{indent}}};");
                output.AppendLine();
            }
            catch (Exception ex)
            {
                string indent = isGlobalNamespace ? "" : "    ";
                string warn = $"Error generating class {type.Name}: {ex.Message}";
                output.AppendLine($"{indent}// {warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static void GenerateCppEnum(Type type, StringBuilder output, bool isGlobalNamespace = false, List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                string enumName = FormatInvalidName(CleanTypeName(type.Name));
                Type underlyingType = Enum.GetUnderlyingType(type);
                string cppUnderlyingType = GetCppType(underlyingType);
                string indent = isGlobalNamespace ? "" : "    ";

                output.AppendLine($"{indent}enum class {enumName} : {cppUnderlyingType} {{");

                var enumValues = Enum.GetValues(type);
                var enumNames = Enum.GetNames(type);
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
                    if (underlyingType == typeof(int))
                    {
                        valueString = ((int)value).ToString();
                    }
                    else if (underlyingType == typeof(uint))
                    {
                        valueString = ((uint)value).ToString() + "u";
                    }
                    else if (underlyingType == typeof(long))
                    {
                        valueString = ((long)value).ToString() + "L";
                    }
                    else if (underlyingType == typeof(ulong))
                    {
                        valueString = ((ulong)value).ToString() + "UL";
                    }
                    else if (underlyingType == typeof(short))
                    {
                        valueString = ((short)value).ToString();
                    }
                    else if (underlyingType == typeof(ushort))
                    {
                        valueString = ((ushort)value).ToString();
                    }
                    else if (underlyingType == typeof(byte))
                    {
                        valueString = ((byte)value).ToString();
                    }
                    else if (underlyingType == typeof(sbyte))
                    {
                        valueString = ((sbyte)value).ToString();
                    }
                    else
                    {
                        valueString = value.ToString();
                    }

                    string formattedValueName = FormatInvalidName(uniqueValueName);
                    if (formattedValueName.ToLower() == "delete")
                    {
                        string warn = $"Enum value '{formattedValueName}' = {valueString} removed because it gives error";
                        output.AppendLine($"{indent}    // {warn}");
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
            catch (Exception ex)
            {
                string indent = isGlobalNamespace ? "" : "    ";
                string warn = $"Error generating enum {type.Name}: {ex.Message}";
                output.AppendLine($"{indent}// {warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static string CleanTypeName(string typeName)
        {
            if (typeName.Contains("`"))
            {
                return typeName.Split('`')[0];
            }
            return typeName;
        }
        static string GetBaseClass(Type type, Type currentClass = null)
        {
            if (type.BaseType == null || type.BaseType == typeof(object)) { return ""; }

            string baseTypeName = CleanTypeName(type.BaseType.Name);

            if (type.BaseType == type)
            {
                return $" : __SELF_REF__{baseTypeName}";
            }

            string[] allowedBaseClasses = {
                "MonoBehaviour", "Behaviour", "Component", "Object", "ScriptableObject", "BNM::UnityEngine::MonoBehaviour"
            };

            if (!allowedBaseClasses.Contains(baseTypeName))
            {
                return $" : __REMOVE__{baseTypeName}";
            }

            if (type.BaseType.Name.Contains("`"))
            {
                return $" : __REMOVE__{baseTypeName}";
            }

            switch (baseTypeName)
            {
                case "MonoBehaviour":
                    return " : BNM::UnityEngine::MonoBehaviour";
                case "Behaviour":
                    return " : Behaviour";
                case "Component":
                    return " : Component";
                case "Object":
                    return " : Object";
                case "ScriptableObject":
                    return "";
                default:
                    return $" : __REMOVE__{baseTypeName}";
            }
        }
        static void GenerateSingletonMethods(Type type, StringBuilder output, HashSet<string> generatedNames, string indent)
        {
            try
            {
                var instanceField = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                var instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

                if (instanceProperty != null)
                {
                    string methodName = "get_Instance";
                    if (!generatedNames.Add(methodName)) return;
                    output.AppendLine($"{indent}    static {FormatInvalidName(CleanTypeName(type.Name))}* {methodName}() {{");
                    output.AppendLine($"{indent}        static Method<{FormatInvalidName(CleanTypeName(type.Name))}*> method = GetClass().GetMethod(O(\"get_Instance\"));");
                    output.AppendLine($"{indent}        return method();");
                    output.AppendLine($"{indent}    }}");
                    output.AppendLine();
                }

                if (instanceField != null)
                {
                    string methodName = "GetInstance";
                    if (!generatedNames.Add(methodName)) return;
                    output.AppendLine($"{indent}    static {FormatInvalidName(CleanTypeName(type.Name))}* {methodName}() {{");
                    output.AppendLine($"{indent}        static Field<{FormatInvalidName(CleanTypeName(type.Name))}*> field = GetClass().GetField(O(\"_instance\"));");
                    output.AppendLine($"{indent}        return field();");
                    output.AppendLine($"{indent}    }}");
                    output.AppendLine();
                }
            }
            catch
            {
            }
        }
        static void GenerateFieldGetter(FieldInfo field, StringBuilder output, Type currentClass = null, string indent = "    ", List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                Type fieldType = field.FieldType;
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    fieldType = Nullable.GetUnderlyingType(fieldType);
                }
                if (ReservedTypeNames.Contains(CleanTypeName(fieldType.Name)))
                {
                    string warn = $"REMOVED: Field '{field.Name}' uses reserved type {fieldType.Name}";
                    output.AppendLine($"{indent}    // {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }
                string cppType = GetCppType(fieldType, currentClass);
                if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__"))
                {
                    string warn = $"{fieldType.Name} is not setup, removed";
                    output.AppendLine($"{indent}    // {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }

                if (field.FieldType.IsClass && field.FieldType != typeof(string) && field.FieldType.Namespace != null && !field.FieldType.Namespace.StartsWith("System") && !field.FieldType.Namespace.StartsWith("UnityEngine") && field.FieldType != currentClass)
                {
                    string warn = $"REMOVED: Field '{field.Name}' uses other class type {CleanTypeName(field.FieldType.Name)}";
                    output.AppendLine($"{indent}    // {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }

                string methodName = $"Get{ToPascalCase(FormatInvalidName(field.Name))}";
                string fieldName = field.Name;
                string fieldVarName = ToCamelCase(FormatInvalidName(field.Name));

                fieldVarName = FormatInvalidName(fieldVarName);

                output.AppendLine($"{indent}    {GetCppType(fieldType, currentClass)} {methodName}() {{");
                output.AppendLine($"{indent}        static Field<{GetCppType(fieldType, currentClass)}> {fieldVarName} = GetClass().GetField(O(\"{fieldName}\"));");

                if (!field.IsStatic)
                {
                    output.AppendLine($"{indent}        {fieldVarName}.SetInstance(this);");
                }

                output.AppendLine($"{indent}        return {fieldVarName}();");
                output.AppendLine($"{indent}    }}");
                output.AppendLine();
            }
            catch (Exception ex)
            {
                string warn = $"Error generating getter for {field.Name}: {ex.Message}";
                output.AppendLine($"{indent}    // {warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static void GenerateFieldSetter(FieldInfo field, StringBuilder output, Type currentClass = null, string indent = "    ", List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                if (field.IsInitOnly) { return; }
                Type fieldType = field.FieldType;
                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    fieldType = Nullable.GetUnderlyingType(fieldType);
                }
                if (ReservedTypeNames.Contains(CleanTypeName(fieldType.Name)))
                {
                    string warn = $"REMOVED: Field '{field.Name}' uses reserved type {fieldType.Name}";
                    output.AppendLine($"{indent}    // {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }
                string cppType = GetCppType(fieldType, currentClass);
                if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__"))
                {
                    string warn = $"{fieldType.Name} is not setup, removed";
                    output.AppendLine($"{indent}    // {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }

                if (field.FieldType.IsClass && field.FieldType != typeof(string) && field.FieldType.Namespace != null && !field.FieldType.Namespace.StartsWith("System") && !field.FieldType.Namespace.StartsWith("UnityEngine") && field.FieldType != currentClass)
                {
                    string warn = $"REMOVED: Field '{field.Name}' uses other class type {CleanTypeName(field.FieldType.Name)}";
                    output.AppendLine($"{indent}    // {warn}");
                    output.AppendLine();
                    warnings.Add(warn);
                    return;
                }

                string methodName = $"Set{ToPascalCase(FormatInvalidName(field.Name))}";
                string fieldName = field.Name;
                string fieldVarName = ToCamelCase(FormatInvalidName(field.Name));

                fieldVarName = FormatInvalidName(fieldVarName);

                output.AppendLine($"{indent}    void {methodName}({GetCppType(fieldType, currentClass)} value) {{");
                output.AppendLine($"{indent}        static Field<{GetCppType(fieldType, currentClass)}> {fieldVarName} = GetClass().GetField(O(\"{fieldName}\"));");

                if (!field.IsStatic)
                {
                    output.AppendLine($"{indent}        {fieldVarName}.SetInstance(this);");
                }

                output.AppendLine($"{indent}        {fieldVarName} = value;");
                output.AppendLine($"{indent}    }}");
                output.AppendLine();
            }
            catch (Exception ex)
            {
                string warn = $"Error generating setter for {field.Name}: {ex.Message}";
                output.AppendLine($"{indent}    // {warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static void GeneratePropertyMethods(Type type, StringBuilder output, HashSet<string> generatedNames, string indent = "    ", List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                BindingFlags propertyFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                var properties = type.GetProperties(propertyFlags).Where(p => !p.Name.Contains("<")).ToArray();

                foreach (var property in properties.OrderBy(p => p.Name))
                {
                    try
                    {
                        if (ReservedTypeNames.Contains(CleanTypeName(property.PropertyType.Name)))
                        {
                            string warn = $"REMOVED: Property '{property.Name}' uses reserved type {property.PropertyType.Name}";
                            output.AppendLine($"{indent}    // {warn}");
                            output.AppendLine();
                            warnings.Add(warn);
                            continue;
                        }
                        string cppType = GetCppType(property.PropertyType, type);
                        if (cppType.Contains("__REMOVE__") || cppType.Contains("__SELF_REF__"))
                        {
                            string warn = $"{property.PropertyType.Name} is not setup, removed";
                            output.AppendLine($"{indent}    // {warn}");
                            output.AppendLine();
                            warnings.Add(warn);
                            continue;
                        }

                        if (property.Name.Contains("."))
                        {
                            string warn = $"REMOVED: Property '{property.Name}' contains dots (fully qualified type name)";
                            output.AppendLine($"{indent}    // {warn}");
                            output.AppendLine();
                            warnings.Add(warn);
                            continue;
                        }

                        if (cppType.Contains("__SELF_REF__"))
                        {
                            string warn = $"REMOVED: Property '{property.Name}' uses its own class type {CleanTypeName(property.PropertyType.Name)}";
                            output.AppendLine($"{indent}    // {warn}");
                            output.AppendLine();
                            warnings.Add(warn);
                            continue;
                        }

                        if (type != null && property.PropertyType.IsClass && property.PropertyType != typeof(string) && property.PropertyType.Namespace != null && !property.PropertyType.Namespace.StartsWith("System") && !property.PropertyType.Namespace.StartsWith("UnityEngine") && property.PropertyType != type)
                        {
                            string warn = $"REMOVED: Property '{property.Name}' uses other class type {CleanTypeName(property.PropertyType.Name)}";
                            output.AppendLine($"{indent}    // {warn}");
                            output.AppendLine();
                            warnings.Add(warn);
                            continue;
                        }

                        string propertyName = property.Name;

                        if (property.CanRead && property.GetMethod != null)
                        {
                            string getterName = $"Get{ToPascalCase(FormatInvalidName(propertyName))}";
                            if (!generatedNames.Add(getterName)) { continue; }
                            output.AppendLine($"{indent}    {cppType} {getterName}() {{");
                            output.AppendLine($"{indent}        static Method<{cppType}> method = GetClass().GetMethod(O(\"get_{propertyName}\"));");
                            if (!property.GetMethod.IsStatic)
                            {
                                output.AppendLine($"{indent}        method.SetInstance(this);");
                            }
                            output.AppendLine($"{indent}        return method();");
                            output.AppendLine($"{indent}    }}");
                            output.AppendLine();
                        }

                        if (property.CanWrite && property.SetMethod != null)
                        {
                            string setterName = $"Set{ToPascalCase(FormatInvalidName(propertyName))}";
                            if (!generatedNames.Add(setterName)) { continue; }
                            output.AppendLine($"{indent}    void {setterName}({cppType} value) {{");
                            output.AppendLine($"{indent}        static Method<void> method = GetClass().GetMethod(O(\"set_{propertyName}\"));");
                            if (!property.SetMethod.IsStatic)
                            {
                                output.AppendLine($"{indent}        method.SetInstance(this);");
                            }
                            output.AppendLine($"{indent}        method(value);");
                            output.AppendLine($"{indent}    }}");
                            output.AppendLine();
                        }
                    }
                    catch (Exception ex)
                    {
                        string warn = $"Error generating property {property.Name}: {ex.Message}";
                        output.AppendLine($"{indent}    // {warn}");
                        output.AppendLine();
                        warnings.Add(warn);
                    }
                }
            }
            catch (Exception ex)
            {
                string warn = $"Error generating properties: {ex.Message}";
                output.AppendLine($"{indent}    // {warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static void GenerateMethodDeclarations(Type type, StringBuilder output, HashSet<string> generatedNames, string indent = "    ", List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            try
            {
                BindingFlags methodFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
                var methods = type.GetMethods(methodFlags).Where(m => !m.IsSpecialName && !m.Name.Contains("<") && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && !m.IsGenericMethod).ToArray();

                foreach (var method in methods.OrderBy(m => m.Name))
                {
                    try
                    {
                        if (ReservedTypeNames.Contains(CleanTypeName(method.ReturnType.Name)))
                        {
                            string warn = $"REMOVED: Method '{method.Name}' return type uses reserved type {method.ReturnType.Name}";
                            output.AppendLine($"{indent}    // {warn}");
                            output.AppendLine();
                            warnings.Add(warn);
                            continue;
                        }
                        string returnType = GetCppType(method.ReturnType, type);
                        if (returnType.Contains("__REMOVE__") || returnType.Contains("__SELF_REF__"))
                        {
                            string warn = $"{method.ReturnType.Name} is not setup, removed";
                            output.AppendLine($"{indent}    // {warn}");
                            output.AppendLine();
                            warnings.Add(warn);
                            continue;
                        }
                        var parameters = method.GetParameters();
                        bool skipMethod = false;
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var paramInfo = parameters[i];
                            if (ReservedTypeNames.Contains(CleanTypeName(paramInfo.ParameterType.Name)))
                            {
                                string warn = $"REMOVED: Method '{method.Name}' parameter '{paramInfo.Name ?? $"param{i}"}' uses reserved type {paramInfo.ParameterType.Name}";
                                output.AppendLine($"{indent}    // {warn}");
                                output.AppendLine();
                                warnings.Add(warn);
                                skipMethod = true;
                                break;
                            }
                            string paramCppType = GetCppType(paramInfo.ParameterType, type);
                            if (paramCppType.Contains("__REMOVE__") || paramCppType.Contains("__SELF_REF__"))
                            {
                                string warn = $"{paramInfo.ParameterType.Name} is not setup, removed";
                                output.AppendLine($"{indent}    // {warn}");
                                output.AppendLine();
                                warnings.Add(warn);
                                skipMethod = true;
                                break;
                            }
                        }
                        if (skipMethod) { continue; }

                        string methodName = FormatInvalidName(method.Name);
                        if (!generatedNames.Add(methodName)) 
                        {
                            string warn = $"SKIPPED: Method '{method.Name}' conflicts with existing method '{methodName}'";
                            output.AppendLine($"{indent}    // {warn}");
                            output.AppendLine();
                            warnings.Add(warn);
                            continue;
                        }
                        var paramList = new List<string>();
                        var validParamNames = MakeValidParams(parameters.Select(p => p.Name ?? $"param{Array.IndexOf(parameters, p)}").ToArray());
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            var paramInfo = parameters[i];
                            var paramType = paramInfo.ParameterType;
                            string cppType;
                            if (paramType.IsByRef)
                            {
                                cppType = GetCppType(paramType.GetElementType(), type) + "*";
                            }
                            else
                            {
                                cppType = GetCppType(paramType, type);
                            }
                            paramList.Add($"{cppType} {validParamNames[i]}");
                        }
                        var formattedParamList = new List<string>();
                        for (int i = 0; i < paramList.Count; i++)
                        {
                            var paramType = paramList[i].Split(' ')[0];
                            var formattedParamName = FormatInvalidName(validParamNames[i]);
                            formattedParamList.Add($"{paramType} {formattedParamName}");
                        }
                        paramList = formattedParamList;

                        string paramString = string.Join(", ", paramList);
                        string methodSignature = $"{indent}    {returnType} {methodName}({paramString}) {{";
                        output.AppendLine(methodSignature);
                        output.AppendLine($"{indent}        static Method<{returnType}> method = GetClass().GetMethod(O(\"{method.Name}\"));");
                        if (!method.IsStatic)
                        {
                            output.AppendLine($"{indent}        method.SetInstance(this);");
                        }
                        if (parameters.Length == 0)
                        {
                            output.AppendLine($"{indent}        return method();");
                        }
                        else
                        {
                                                    var formattedParamNames = validParamNames.Select(p => FormatInvalidName(p)).ToArray();
                        output.AppendLine($"{indent}        return method({string.Join(", ", formattedParamNames)});");
                        }
                        output.AppendLine($"{indent}    }}");
                        output.AppendLine();
                    }
                    catch (Exception ex)
                    {
                        string warn = $"Error generating method {method.Name}: {ex.Message}";
                        output.AppendLine($"{indent}    // {warn}");
                        output.AppendLine();
                        warnings.Add(warn);
                    }
                }
            }
            catch (Exception ex)
            {
                string warn = $"Error generating methods: {ex.Message}";
                output.AppendLine($"{indent}    // {warn}");
                output.AppendLine();
                warnings.Add(warn);
            }
        }
        static string GetCppType(Type type, Type currentClass = null)
        {
            try
            {
                if (type.Namespace != null && !type.Namespace.StartsWith("System") && !type.Namespace.StartsWith("UnityEngine"))
                {
                    return $"__REMOVE__{type.Name}";
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
                {
                    type = Nullable.GetUnderlyingType(type);
                }

                if (currentClass != null && type == currentClass)
                {
                    return $"__SELF_REF__{CleanTypeName(type.Name)}*";
                }

                if (currentClass != null && type.IsClass && type != typeof(string) && type.Namespace != null && !type.Namespace.StartsWith("System") && !type.Namespace.StartsWith("UnityEngine") && type != currentClass)
                {
                    return $"__REMOVE__{CleanTypeName(type.Name)}";
                }

                if (type.IsArray)
                {
                    Type elementType = type.GetElementType();
                    string elementCppType = GetCppType(elementType, currentClass);
                    return $"BNM::Structures::Mono::Array<{elementCppType}>*";
                }

                if (type.IsGenericType)
                {
                    try
                    {
                        string genericTypeName = type.Name.Split('`')[0];
                        var genericArgs = type.GetGenericArguments();

                        switch (genericTypeName)
                        {
                            case "List":
                                if (genericArgs.Length > 0)
                                {
                                    string elementType = GetCppType(genericArgs[0], currentClass);
                                    if (!elementType.Contains("__REMOVE__") && !elementType.Contains("__SELF_REF__"))
                                    {
                                        return $"BNM::Structures::Mono::List<{elementType}>*";
                                    }
                                }
                                return "void*";
                            default:
                                return "void*";
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not process generic type {type.Name}: {ex.Message}");
                        return "void*";
                    }
                }

                if (type.IsEnum)
                {
                    if (type.Namespace != null && !type.Namespace.StartsWith("System") && !type.Namespace.StartsWith("UnityEngine"))
                    {
                        return $"__REMOVE__{CleanTypeName(type.Name)}";
                    }

                    if (type.Namespace == null && !definedTypes.Contains(CleanTypeName(type.Name)))
                    {
                        return $"__REMOVE__{CleanTypeName(type.Name)}";
                    }

                    if (type.Namespace != null && !type.Namespace.StartsWith("System"))
                    {
                        if (definedTypes.Contains(CleanTypeName(type.Name)))
                        {
                            return $"{type.Namespace.Replace(".", "::")}::{CleanTypeName(type.Name)}";
                        }
                        else
                        {
                            return "int";
                        }
                    }
                    return CleanTypeName(type.Name);
                }

                switch (type.FullName)
                {
                    case "System.Void": return "void";
                    case "System.Int8": return "int8_t";
                    case "System.UInt8": return "uint8_t";
                    case "System.Int16": return "short";
                    case "System.Int32": return "int";
                    case "System.Int64": return "int64_t";
                    case "System.Single": return "float";
                    case "System.Double": return "double";
                    case "System.Boolean": return "bool";
                    case "System.Char": return "char";
                    case "System.UInt16": return "BNM::Types::ushort";
                    case "System.UInt32": return "BNM::Types::uint";
                    case "System.UInt64": return "BNM::Types::ulong";
                    case "System.Decimal": return "BNM::Types::decimal";
                    case "System.Byte": return "BNM::Types::byte";
                    case "System.SByte": return "BNM::Types::sbyte";
                    case "System.String": return "BNM::Structures::Mono::String*";
                    case "System.Type": return "BNM::MonoType*";
                    case "System.IntPtr": return "BNM::Types::nuint";
                    case "UnityEngine.Object": return "BNM::UnityEngine::Object*";
                    case "UnityEngine.MonoBehaviour": return "BNM::UnityEngine::MonoBehaviour*";
                    case "UnityEngine.Vector2": return "BNM::Structures::Unity::Vector2";
                    case "UnityEngine.Vector3": return "BNM::Structures::Unity::Vector3";
                    case "UnityEngine.Vector4": return "BNM::Structures::Unity::Vector4";
                    case "UnityEngine.Quaternion": return "BNM::Structures::Unity::Quaternion";
                    case "UnityEngine.Rect": return "BNM::Structures::Unity::Rect";
                    case "UnityEngine.Color": return "BNM::Structures::Unity::Color";
                    case "UnityEngine.Color32": return "BNM::Structures::Unity::Color32";
                    case "UnityEngine.Ray": return "BNM::Structures::Unity::Ray";
                    case "UnityEngine.RaycastHit": return "BNM::Structures::Unity::RaycastHit";
                }

                switch (CleanTypeName(type.Name))
                {
                    case "Void": return "void";
                    case "Boolean": return "bool";
                    case "Int32": return "int";
                    case "UInt32": return "unsigned int";
                    case "Int64": return "long long";
                    case "UInt64": return "unsigned long long";
                    case "Int16": return "short";
                    case "UInt16": return "unsigned short";
                    case "Byte": return "unsigned char";
                    case "SByte": return "signed char";
                    case "Single": return "float";
                    case "Double": return "double";
                    case "String": return "String*";
                    case "Vector3": return "Vector3";
                    case "Vector2": return "Vector2";
                    case "Vector4": return "Vector4";
                    case "Quaternion": return "Quaternion";
                    case "Color": return "Color";
                    case "Transform": return "Transform*";
                    case "GameObject": return "GameObject*";
                    case "Rigidbody": return "Rigidbody*";
                    case "Collider": return "Collider*";
                    case "SphereCollider": return "SphereCollider*";
                    case "BoxCollider": return "BoxCollider*";
                    case "Component": return "Component*";
                    case "MonoBehaviour": return "BNM::UnityEngine::MonoBehaviour*";
                    case "Behaviour": return "Behaviour*";
                    case "Object": return "Object*";
                    case "Material": return "Material*";
                    case "Texture2D": return "Texture2D*";
                    case "AudioClip": return "AudioClip*";
                    case "AudioSource": return "AudioSource*";
                    case "Camera": return "Camera*";
                    case "Light": return "Light*";
                    case "MeshRenderer": return "MeshRenderer*";
                    case "Renderer": return "Renderer*";
                    case "ParticleSystem": return "ParticleSystem*";
                    default:
                        return $"__REMOVE__{CleanTypeName(type.Name)}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error processing type {type.Name}: {ex.Message}");
                return "void*";
            }
        }
        static string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) { return input; }

            var parts = input.Split('_');
            var result = new StringBuilder();

            foreach (var part in parts)
            {
                if (!string.IsNullOrEmpty(part))
                {
                    result.Append(char.ToUpper(part[0]));
                    if (part.Length > 1) { result.Append(part.Substring(1)); }
                }
            }

            return result.ToString();
        }
        static string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input)) { return input; }

            string pascalCase = ToPascalCase(input);
            if (pascalCase.Length > 0)
            {
                return char.ToLower(pascalCase[0]) + pascalCase.Substring(1);
            }

            return pascalCase;
        }
        static bool StartsWithNumber(string str)
        {
            if (string.IsNullOrEmpty(str)) return false;
            return char.IsDigit(str[0]);
        }
        static bool IsKeyword(string str)
        {
            string[] keywords = new string[] {
                "", "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel", "atomic_commit", "atomic_noexcept",
                "auto", "bitand", "bitor", "bool", "break", "case", "catch", "char", "char8_t", "char16_t", "char32_t",
                "class", "compl", "concept", "const", "consteval", "constexpr", "constinit", "const_cast", "continue",
                "contract_assert", "co_await", "co_return", "co_yield", "decltype", "default", "delete", "do", "double",
                "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto",
                "if", "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq", "nullptr",
                "operator", "or", "or_eq", "private", "protected", "public", "reflexpr", "register", "reinterpret_cast",
                "requires", "return", "short", "signed", "sizeof", "static", "static_assert", "static_cast", "struct",
                "switch", "synchronized", "template", "this", "thread_local", "throw", "true", "try", "typedef", "typeid",
                "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq",

                "abstract", "add", "as", "base", "byte", "checked", "decimal", "delegate", "event", "explicit", "extern",
                "finally", "fixed", "foreach", "implicit", "in", "interface", "internal", "is", "lock", "null", "object",
                "out", "override", "params", "readonly", "ref", "remove", "sbyte", "sealed", "stackalloc", "string",
                "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using static", "value", "when", "where",
                "yield",

                "INT32_MAX", "INT32_MIN", "UINT32_MAX", "UINT16_MAX", "INT16_MAX", "UINT8_MAX", "INT8_MAX", "INT_MAX",
                "Assert", "NULL", "O",
            };
            return keywords.Contains(str);
        }
        static string[] MakeValidParams(string[] paramNames)
        {
            var results = new List<string>();
            var seen = new Dictionary<string, int>();

            foreach (var param in paramNames)
            {
                string cparam = param;
                if (seen.ContainsKey(param))
                {
                    seen[param]++;
                    cparam = $"{param}_{seen[param]}";
                }
                else
                {
                    seen[param] = 0;
                }

                results.Add(cparam);
            }

            return results.ToArray();
        }
        static string FormatInvalidName(string className)
        {
            string str = className.Trim()
                .Replace("<", "$")
                .Replace(">", "$")
                .Replace("|", "$")
                .Replace("-", "$")
                .Replace("`", "$")
                .Replace("=", "$")
                .Replace("@", "$")
                .Trim();
            
            if (string.IsNullOrEmpty(str))
                return "_";

            if (StartsWithNumber(str))
                str = "_" + str;

            if (IsKeyword(str))
                str = "$" + str;

            return str;
        }
        static string GetEnumUnderlyingType(Type enumType)
        {
            try
            {
                Type underlyingType = Enum.GetUnderlyingType(enumType);
                return GetCppType(underlyingType);
            }
            catch
            {
                return "int";
            }
        }
        static void ValidateGeneratedCode(string outputPath)
        {
            try
            {
                Console.WriteLine("Validating generated C++ code...");

                var allErrors = new List<string>();
                var allWarnings = new List<string>();

                var files = new List<string>();
                if (Directory.Exists(outputPath))
                {
                    files = Directory.GetFiles(outputPath, "*.hpp", SearchOption.AllDirectories).ToList();
                }
                else if (File.Exists(outputPath))
                {
                    files = new List<string> { outputPath };
                }
                foreach (var file in files)
                {
                    try
                    {
                        var fileErrors = new List<string>();
                        var fileWarnings = new List<string>();

                        var lines = File.ReadAllLines(file);
                        int lineNumber = 1;
                        int braceCount = 0;
                        int parenthesisCount = 0;
                        int bracketCount = 0;

                        foreach (var line in lines)
                        {
                            try
                            {
                                foreach (char c in line)
                                {
                                    switch (c)
                                    {
                                        case '{': braceCount++; break;
                                        case '}': braceCount--; break;
                                        case '(': parenthesisCount++; break;
                                        case ')': parenthesisCount--; break;
                                        case '[': bracketCount++; break;
                                        case ']': bracketCount--; break;
                                    }
                                }

                                if (line.Contains("struct") && line.Contains(":") && !line.Contains("{"))
                                {
                                    fileWarnings.Add($"Line {lineNumber}: Struct declaration might be missing opening brace");
                                }

                                if (line.Contains("Method<") && !line.Contains("GetMethod"))
                                {
                                    fileWarnings.Add($"Line {lineNumber}: Method template might be missing proper instantiation");
                                }

                                if (line.Contains("Field<") && !line.Contains("GetField"))
                                {
                                    fileWarnings.Add($"Line {lineNumber}: Field template might be missing proper instantiation");
                                }

                                if (line.Contains("::") && line.Contains("*") && !line.Contains("BNM::") && !line.Contains("Mono::"))
                                {
                                    fileWarnings.Add($"Line {lineNumber}: Potential namespace issue with pointer type");
                                }

                                if (line.Trim().EndsWith("()") && !line.Contains(";") && !line.Contains("{"))
                                {
                                    fileWarnings.Add($"Line {lineNumber}: Method call might be missing semicolon");
                                }

                                lineNumber++;
                            }
                            catch (Exception ex)
                            {
                                fileErrors.Add($"Line {lineNumber}: Error parsing line: {ex.Message}");
                                lineNumber++;
                            }
                        }

                        if (braceCount != 0)
                        {
                            fileErrors.Add($"Unmatched braces: {braceCount} more {(braceCount > 0 ? "opening" : "closing")} braces");
                        }

                        if (parenthesisCount != 0)
                        {
                            fileErrors.Add($"Unmatched parentheses: {parenthesisCount} more {(parenthesisCount > 0 ? "opening" : "closing")} parentheses");
                        }

                        if (bracketCount != 0)
                        {
                            fileErrors.Add($"Unmatched brackets: {bracketCount} more {(bracketCount > 0 ? "opening" : "closing")} brackets");
                        }

                        var fileContent = File.ReadAllText(file);
                        if (!fileContent.Contains("#pragma once"))
                        {
                            fileWarnings.Add("Missing #pragma once directive");
                        }

                        if (!fileContent.Contains("using namespace BNM"))
                        {
                            fileWarnings.Add("Missing BNM namespace usage");
                        }

                        if (fileErrors.Count > 0)
                        {
                            Console.WriteLine($"Found {fileErrors.Count} validation errors in {file}:");
                            foreach (var error in fileErrors)
                            {
                                Console.WriteLine($"   - {error}");
                            }
                            allErrors.AddRange(fileErrors.Select(e => $"{file}: {e}"));
                        }

                        if (fileWarnings.Count > 0)
                        {
                            Console.WriteLine($"Found {fileWarnings.Count} validation warnings in {file}:");
                            foreach (var warning in fileWarnings.Take(5))
                            {
                                Console.WriteLine($"   - {warning}");
                            }
                            if (fileWarnings.Count > 5)
                            {
                                Console.WriteLine($"   ... and {fileWarnings.Count - 5} more warnings");
                            }
                            allWarnings.AddRange(fileWarnings.Select(w => $"{file}: {w}"));
                        }
                    }
                    catch (Exception ex)
                    {
                        allErrors.Add($"Error validating {file}: {ex.Message}");
                    }
                }

                if (allErrors.Count > 0)
                {
                    Console.WriteLine($"Found {allErrors.Count} validation errors across all files:");
                    string validationErrorsPath = Directory.Exists(outputPath) ? Path.Combine(outputPath, "ValidationErrors.txt") : Path.Combine(Path.GetDirectoryName(outputPath), "ValidationErrors.txt");
                    File.WriteAllText(validationErrorsPath, string.Join("\n", allErrors));
                    Console.WriteLine($"Validation errors saved to: {validationErrorsPath}");
                }
                else
                {
                    Console.WriteLine("No validation errors found");
                }

                if (allWarnings.Count > 0)
                {
                    Console.WriteLine($"Found {allWarnings.Count} validation warnings across all files:");
                    string validationWarningsPath = Directory.Exists(outputPath) ? Path.Combine(outputPath, "ValidationWarnings.txt") : Path.Combine(Path.GetDirectoryName(outputPath), "ValidationWarnings.txt");
                    File.WriteAllText(validationWarningsPath, string.Join("\n", allWarnings));
                    Console.WriteLine($"Validation warnings saved to: {validationWarningsPath}");
                }
                else
                {
                    Console.WriteLine("No validation warnings found");
                }

                Console.WriteLine("C++ code validation completed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not validate generated code: {ex.Message}");
            }
        }
        private static string SafeGetNamespace(Type t)
        {
            try
            {
                return t.Namespace ?? "Global";
            }
            catch
            {
                return null;
            }
        }
    }
}