using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;

namespace BNMCppHeaderGenerator
{
    internal class Program
    {
        private static HashSet<string> definedTypes = new HashSet<string>();

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
                                               t.IsPublic &&
                                               !t.Name.Contains("<") &&
                                               !t.Name.StartsWith("_") &&
                                               t.Namespace != null).ToList();

                Console.WriteLine($"Found {classes.Count} classes and enums to process...");

                var groupedClasses = classes.GroupBy(t => t.Namespace).OrderBy(g => g.Key);

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
            output.AppendLine();

            foreach (var type in classes)
            {
                definedTypes.Add(CleanTypeName(type.Name));
            }

            output.AppendLine("// Forward declarations");
            foreach (var namespaceGroup in groupedClasses)
            {
                string namespaceName = namespaceGroup.Key;
                output.AppendLine($"namespace {namespaceName.Replace(".", "::")} {{");
                
                var sortedClasses = namespaceGroup.OrderBy(t => t.Name);
                foreach (var type in sortedClasses)
                {
                    if (type.IsEnum)
                    {
                        output.AppendLine($"    enum class {CleanTypeName(type.Name)};");
                    }
                    else
                    {
                        output.AppendLine($"    struct {CleanTypeName(type.Name)};");
                    }
                }
                
                output.AppendLine("}");
                output.AppendLine();
            }

            foreach (var namespaceGroup in groupedClasses)
            {
                string namespaceName = namespaceGroup.Key;
                output.AppendLine($"namespace {namespaceName.Replace(".", "::")} {{");

                var sortedClasses = namespaceGroup.OrderBy(t => t.Name);

                foreach (var type in sortedClasses)
                {
                    if (type.Namespace != null && (type.Namespace.StartsWith("BoingKit") || type.Namespace.StartsWith("CjLib") || type.Namespace.StartsWith("TMPro") || type.Namespace.StartsWith("GorillaNetworking") || type.Namespace.StartsWith("emotitron") || type.Namespace.StartsWith("Unity.Collections")))
                    {
                        output.AppendLine($"    // {type.FullName} is not setup, removed");
                        output.AppendLine();
                        continue;
                    }
                    
                    Console.WriteLine($"Processing: {type.FullName}");
                    
                    if (type.IsEnum)
                    {
                        GenerateCppEnum(type, output);
                    }
                    else
                    {
                        GenerateCppClass(type, output);
                    }
                }

                output.AppendLine("}");
                output.AppendLine();
            }

            string outputPath = Path.Combine(outputDir, "BNMResolves.hpp");
            File.WriteAllText(outputPath, output.ToString());
            
            Console.WriteLine();
            Console.WriteLine($"C++ headers saved to: {outputPath}");
            
            ValidateGeneratedCode(outputPath);
        }
        static void GenerateFolderStructure(List<Type> classes, IGrouping<string, Type>[] groupedClasses, string outputDir)
        {
            foreach (var namespaceGroup in groupedClasses)
            {
                string namespaceName = namespaceGroup.Key;
                string namespacePath = namespaceName.Replace(".", "/");
                string namespaceDir = Path.Combine(outputDir, namespacePath);
                
                Directory.CreateDirectory(namespaceDir);

                var sortedClasses = namespaceGroup.OrderBy(t => t.Name);
                
                foreach (var type in sortedClasses)
                {
                    definedTypes.Add(CleanTypeName(type.Name));
                }

                foreach (var type in sortedClasses)
                {
                    if (type.Namespace != null && (type.Namespace.StartsWith("BoingKit") || type.Namespace.StartsWith("CjLib") || type.Namespace.StartsWith("TMPro") || type.Namespace.StartsWith("GorillaNetworking") || type.Namespace.StartsWith("emotitron") || type.Namespace.StartsWith("Unity.Collections")))
                    {
                        Console.WriteLine($"Skipping: {type.FullName} (removed)");
                        continue;
                    }
                    
                    Console.WriteLine($"Processing: {type.FullName}");
                    
                    var classContent = new StringBuilder();
                    classContent.AppendLine("#pragma once");
                    classContent.AppendLine("using namespace BNM;");
                    classContent.AppendLine("using namespace BNM::IL2CPP;");
                    classContent.AppendLine("using namespace BNM::Structures;");
                    classContent.AppendLine("using namespace BNM::Structures::Unity;");
                    classContent.AppendLine("using namespace BNM::UnityEngine;");
                    classContent.AppendLine("#define O(str) BNM_OBFUSCATE(str)");
                    classContent.AppendLine();

                    classContent.AppendLine($"namespace {namespaceName.Replace(".", "::")} {{");
                    classContent.AppendLine();

                    var otherTypesInNamespace = sortedClasses.Where(t => t != type).ToList();
                    if (otherTypesInNamespace.Any())
                    {
                        classContent.AppendLine("    // Forward declarations for other types in this namespace");
                        foreach (var otherType in otherTypesInNamespace)
                        {
                            if (otherType.IsEnum)
                            {
                                classContent.AppendLine($"    enum class {CleanTypeName(otherType.Name)};");
                            }
                            else
                            {
                                classContent.AppendLine($"    struct {CleanTypeName(otherType.Name)};");
                            }
                        }
                        classContent.AppendLine();
                    }

                    if (type.IsEnum)
                    {
                        GenerateCppEnum(type, classContent);
                    }
                    else
                    {
                        GenerateCppClass(type, classContent);
                    }

                    classContent.AppendLine("}");

                    string className = CleanTypeName(type.Name);
                    string classFilePath = Path.Combine(namespaceDir, $"{className}.hpp");
                    File.WriteAllText(classFilePath, classContent.ToString());
                }
            }

            Console.WriteLine();
            Console.WriteLine($"C++ headers saved to: {outputDir}/");
            
            ValidateGeneratedCode(outputDir);
        }
        static void GenerateCppClass(Type type, StringBuilder output)
        {
            try
            {
                string baseClass = GetBaseClass(type, type);
                string className = CleanTypeName(type.Name);

                if (baseClass.Contains("__REMOVE__"))
                {
                    output.AppendLine($"    // REMOVED: Class '{className}' inherits from removed base class");
                    output.AppendLine();
                    return;
                }

                if (baseClass.Contains("__SELF_REF__"))
                {
                    output.AppendLine($"    // REMOVED: Class '{className}' inherits from its own class type");
                    output.AppendLine($"    struct {className} : Behaviour {{");
                }
                else if (type.BaseType != null && type.BaseType.IsClass && type.BaseType != typeof(string) && type.BaseType.Namespace != null && !type.BaseType.Namespace.StartsWith("System") && !type.BaseType.Namespace.StartsWith("UnityEngine") && type.BaseType != type)
                {
                    output.AppendLine($"    // NOTE: Class '{className}' inherits from other class type '{CleanTypeName(type.BaseType.Name)}'");
                    output.AppendLine($"    struct {className}{baseClass} {{");
                }
                else if (!string.IsNullOrEmpty(baseClass))
                {
                    output.AppendLine($"    struct {className}{baseClass} {{");
                }
                else
                {
                    output.AppendLine($"    struct {className} : Behaviour {{");
                }
                output.AppendLine("    public:");

                var generatedNames = new HashSet<string>();

                output.AppendLine("        static Class GetClass() {");
                output.AppendLine($"            const char* className = \"{type.Name}\";");
                output.AppendLine($"            static BNM::Class clahh = Class(O(\"{type.Namespace}\"), O(className), Image(O(\"Assembly-CSharp.dll\")));");
                output.AppendLine("            return clahh;");
                output.AppendLine("        }");
                output.AppendLine();

                output.AppendLine("        static MonoType* GetType() {");
                output.AppendLine("            return GetClass().GetMonoType();");
                output.AppendLine("        }");
                output.AppendLine();

                BindingFlags fieldFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                var fields = type.GetFields(fieldFlags).Where(f => !f.IsLiteral && !f.Name.Contains("<")).ToArray();

                GenerateSingletonMethods(type, output, generatedNames);

                foreach (var field in fields.OrderBy(f => f.Name))
                {
                    string getterName = $"Get{ToPascalCase(field.Name)}";
                    if (!generatedNames.Add(getterName)) continue;
                    GenerateFieldGetter(field, output, type);
                }

                foreach (var field in fields.OrderBy(f => f.Name))
                {
                    string setterName = $"Set{ToPascalCase(field.Name)}";
                    if (!generatedNames.Add(setterName)) continue;
                    GenerateFieldSetter(field, output, type);
                }

                GeneratePropertyMethods(type, output, generatedNames);
                GenerateMethodDeclarations(type, output, generatedNames);

                output.AppendLine("    };\n");
            }
            catch (Exception ex)
            {
                output.AppendLine($"    // Error generating class {type.Name}: {ex.Message}");
                output.AppendLine();
            }
        }
        static void GenerateCppEnum(Type type, StringBuilder output)
        {
            try
            {
                string enumName = CleanTypeName(type.Name);
                Type underlyingType = Enum.GetUnderlyingType(type);
                string cppUnderlyingType = GetCppType(underlyingType);

                output.AppendLine($"    enum class {enumName} : {cppUnderlyingType} {{");

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
                    
                    if (uniqueValueName.ToLower() == "delete")
                    {
                        output.AppendLine($"        // {uniqueValueName} = {valueString}, // removed \"delete\" bc it gives error");
                    }
                    else
                    {
                        output.AppendLine($"        {uniqueValueName} = {valueString},");
                    }
                }

                output.AppendLine("    };");
                output.AppendLine();
            }
            catch (Exception ex)
            {
                output.AppendLine($"    // Error generating enum {type.Name}: {ex.Message}");
                output.AppendLine();
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
        static void GenerateSingletonMethods(Type type, StringBuilder output, HashSet<string> generatedNames)
        {
            try
            {
                var instanceField = type.GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static);
                var instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

                if (instanceProperty != null)
                {
                    string methodName = "get_Instance";
                    if (!generatedNames.Add(methodName)) return;
                    output.AppendLine($"        static {CleanTypeName(type.Name)}* {methodName}() {{");
                    output.AppendLine($"            static Method<{CleanTypeName(type.Name)}*> method = GetClass().GetMethod(O(\"get_Instance\"));");
                    output.AppendLine("            return method();");
                    output.AppendLine("        }");
                    output.AppendLine();
                }

                if (instanceField != null)
                {
                    string methodName = "GetInstance";
                    if (!generatedNames.Add(methodName)) return;
                    output.AppendLine($"        static {CleanTypeName(type.Name)}* {methodName}() {{");
                    output.AppendLine($"            static Field<{CleanTypeName(type.Name)}*> field = GetClass().GetField(O(\"_instance\"));");
                    output.AppendLine("            return field();");
                    output.AppendLine("        }");
                    output.AppendLine();
                }
            }
            catch
            {
            }
        }
        static void GenerateFieldGetter(FieldInfo field, StringBuilder output, Type currentClass = null)
        {
            try
            {
                string cppType = GetCppType(field.FieldType, currentClass);
                if (cppType.Contains("__REMOVE__"))
                {
                    output.AppendLine($"        // {field.FieldType.Name} is not setup, removed");
                    output.AppendLine();
                    return;
                }
                
                if (cppType.Contains("__SELF_REF__"))
                {
                    output.AppendLine($"        // REMOVED: Field '{field.Name}' uses its own class type {CleanTypeName(field.FieldType.Name)}");
                    output.AppendLine();
                    return;
                }
                
                if (currentClass != null && field.FieldType.IsClass && field.FieldType != typeof(string) && field.FieldType.Namespace != null && !field.FieldType.Namespace.StartsWith("System") && !field.FieldType.Namespace.StartsWith("UnityEngine") && field.FieldType != currentClass)
                {
                    output.AppendLine($"        // REMOVED: Field '{field.Name}' uses other class type {CleanTypeName(field.FieldType.Name)}");
                    output.AppendLine();
                    return;
                }
                
                string methodName = $"Get{ToPascalCase(field.Name)}";
                string fieldName = field.Name;
                string fieldVarName = ToCamelCase(field.Name);

                output.AppendLine($"        {cppType} {methodName}() {{");
                output.AppendLine($"            static Field<{cppType}> {fieldVarName} = GetClass().GetField(O(\"{fieldName}\"));");

                if (!field.IsStatic)
                {
                    output.AppendLine($"            {fieldVarName}.SetInstance(this);");
                }

                output.AppendLine($"            return {fieldVarName}();");
                output.AppendLine("        }");
                output.AppendLine();
            }
            catch (Exception ex)
            {
                output.AppendLine($"        // Error generating getter for {field.Name}: {ex.Message}");
                output.AppendLine();
            }
        }
        static void GenerateFieldSetter(FieldInfo field, StringBuilder output, Type currentClass = null)
        {
            try
            {
                if (field.IsInitOnly) { return; }

                string cppType = GetCppType(field.FieldType, currentClass);
                if (cppType.Contains("__REMOVE__"))
                {
                    output.AppendLine($"        // {field.FieldType.Name} is not setup, removed");
                    output.AppendLine();
                    return;
                }
                
                if (cppType.Contains("__SELF_REF__"))
                {
                    output.AppendLine($"        // REMOVED: Field '{field.Name}' uses its own class type {CleanTypeName(field.FieldType.Name)}");
                    output.AppendLine();
                    return;
                }
                
                if (currentClass != null && field.FieldType.IsClass && field.FieldType != typeof(string) && field.FieldType.Namespace != null && !field.FieldType.Namespace.StartsWith("System") &&  !field.FieldType.Namespace.StartsWith("UnityEngine") && field.FieldType != currentClass)
                {
                    output.AppendLine($"        // REMOVED: Field '{field.Name}' uses other class type {CleanTypeName(field.FieldType.Name)}");
                    output.AppendLine();
                    return;
                }
                
                string methodName = $"Set{ToPascalCase(field.Name)}";
                string fieldName = field.Name;
                string fieldVarName = ToCamelCase(field.Name);

                output.AppendLine($"        void {methodName}({cppType} value) {{");
                output.AppendLine($"            static Field<{cppType}> {fieldVarName} = GetClass().GetField(O(\"{fieldName}\"));");

                if (!field.IsStatic)
                {
                    output.AppendLine($"            {fieldVarName}.SetInstance(this);");
                }

                output.AppendLine($"            {fieldVarName} = value;");
                output.AppendLine("        }");
                output.AppendLine();
            }
            catch (Exception ex)
            {
                output.AppendLine($"        // Error generating setter for {field.Name}: {ex.Message}");
                output.AppendLine();
            }
        }
        static void GeneratePropertyMethods(Type type, StringBuilder output, HashSet<string> generatedNames)
        {
            try
            {
                BindingFlags propertyFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                var properties = type.GetProperties(propertyFlags).Where(p => !p.Name.Contains("<")).ToArray();

                foreach (var property in properties.OrderBy(p => p.Name))
                {
                    try
                    {
                        if (property.Name.Contains("."))
                        {
                            output.AppendLine($"        // REMOVED: Property '{property.Name}' contains dots (fully qualified type name)");
                            output.AppendLine();
                            continue;
                        }
                        
                        string cppType = GetCppType(property.PropertyType, type);
                        if (cppType.Contains("__REMOVE__"))
                        {
                            output.AppendLine($"        // {property.PropertyType.Name} is not setup, removed");
                            output.AppendLine();
                            continue;
                        }
                        
                        if (cppType.Contains("__SELF_REF__"))
                        {
                            output.AppendLine($"        // REMOVED: Property '{property.Name}' uses its own class type {CleanTypeName(property.PropertyType.Name)}");
                            output.AppendLine();
                            continue;
                        }
                        
                        if (type != null && property.PropertyType.IsClass && property.PropertyType != typeof(string) && property.PropertyType.Namespace != null && !property.PropertyType.Namespace.StartsWith("System") &&  !property.PropertyType.Namespace.StartsWith("UnityEngine") && property.PropertyType != type)
                        {
                            output.AppendLine($"        // REMOVED: Property '{property.Name}' uses other class type {CleanTypeName(property.PropertyType.Name)}");
                            output.AppendLine();
                            continue;
                        }
                        
                        string propertyName = property.Name;

                        if (property.CanRead && property.GetMethod != null)
                        {
                            string getterName = $"Get{ToPascalCase(propertyName)}";
                            if (!generatedNames.Add(getterName)) continue;
                            output.AppendLine($"        {cppType} {getterName}() {{");
                            output.AppendLine($"            static Method<{cppType}> method = GetClass().GetMethod(O(\"get_{propertyName}\"));");
                            if (!property.GetMethod.IsStatic)
                            {
                                output.AppendLine("            method.SetInstance(this);");
                            }
                            output.AppendLine("            return method();");
                            output.AppendLine("        }");
                            output.AppendLine();
                        }

                        if (property.CanWrite && property.SetMethod != null)
                        {
                            string setterName = $"Set{ToPascalCase(propertyName)}";
                            if (!generatedNames.Add(setterName)) continue;
                            output.AppendLine($"        void {setterName}({cppType} value) {{");
                            output.AppendLine($"            static Method<void> method = GetClass().GetMethod(O(\"set_{propertyName}\"));");
                            if (!property.SetMethod.IsStatic)
                            {
                                output.AppendLine("            method.SetInstance(this);");
                            }
                            output.AppendLine("            method(value);");
                            output.AppendLine("        }");
                            output.AppendLine();
                        }
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine($"        // Error generating property {property.Name}: {ex.Message}");
                        output.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                output.AppendLine($"        // Error generating properties: {ex.Message}");
                output.AppendLine();
            }
        }
        static void GenerateMethodDeclarations(Type type, StringBuilder output, HashSet<string> generatedNames)
        {
            try
            {
                BindingFlags methodFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
                var methods = type.GetMethods(methodFlags).Where(m => !m.IsSpecialName && !m.Name.Contains("<") && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && !m.IsGenericMethod).ToArray();

                foreach (var method in methods.OrderBy(m => m.Name))
                {
                    try
                    {
                        if (method.Name.Contains("."))
                        {
                            output.AppendLine($"        // REMOVED: Method '{method.Name}' contains dots (fully qualified type name)");
                            output.AppendLine();
                            continue;
                        }
                        
                        string returnType = GetCppType(method.ReturnType, type);
                        if (returnType.Contains("__REMOVE__"))
                        {
                            output.AppendLine($"        // {method.ReturnType.Name} is not setup, removed");
                            output.AppendLine();
                            continue;
                        }
                        
                        if (returnType.Contains("__SELF_REF__"))
                        {
                            output.AppendLine($"        // REMOVED: Method '{method.Name}' return type uses its own class type {CleanTypeName(method.ReturnType.Name)}");
                            output.AppendLine();
                            continue;
                        }
                        
                        if (type != null && method.ReturnType.IsClass && method.ReturnType != typeof(string) && method.ReturnType.Namespace != null && !method.ReturnType.Namespace.StartsWith("System") &&  !method.ReturnType.Namespace.StartsWith("UnityEngine") && method.ReturnType != type)
                        {
                            output.AppendLine($"        // REMOVED: Method '{method.Name}' return type uses other class type {CleanTypeName(method.ReturnType.Name)}");
                            output.AppendLine();
                            continue;
                        }
                        
                        string methodName = method.Name;
                        if (!generatedNames.Add(methodName)) continue;
                        var parameters = method.GetParameters();

                        var paramList = new List<string>();
                        bool skipMethod = false;
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
                            if (cppType.Contains("__REMOVE__"))
                            {
                                output.AppendLine($"        // {paramType.Name} is not setup, removed");
                                output.AppendLine();
                                skipMethod = true;
                                break;
                            }
                            
                            if (cppType.Contains("__SELF_REF__"))
                            {
                                output.AppendLine($"        // REMOVED: Method '{method.Name}' parameter '{paramInfo.Name ?? $"param{i}"}' uses its own class type {CleanTypeName(paramType.Name)}");
                                output.AppendLine();
                                skipMethod = true;
                                break;
                            }
                            
                            if (type != null && paramType.IsClass && paramType != typeof(string) && paramType.Namespace != null && !paramType.Namespace.StartsWith("System") && !paramType.Namespace.StartsWith("UnityEngine") && paramType != type)
                            {
                                output.AppendLine($"        // REMOVED: Method '{method.Name}' parameter '{paramInfo.Name ?? $"param{i}"}' uses other class type {CleanTypeName(paramType.Name)}");
                                output.AppendLine();
                                skipMethod = true;
                                break;
                            }
                            
                            string paramName = paramInfo.Name ?? $"param{i}";
                            paramList.Add($"{cppType} {paramName}");
                        }
                        if (skipMethod) continue;

                        string paramString = string.Join(", ", paramList);
                        string methodSignature = $"        {returnType} {methodName}({paramString}) {{";
                        output.AppendLine(methodSignature);
                        output.AppendLine($"            static Method<{returnType}> method = GetClass().GetMethod(O(\"{methodName}\"));");
                        if (!method.IsStatic)
                        {
                            output.AppendLine("            method.SetInstance(this);");
                        }
                        if (parameters.Length == 0)
                        {
                            output.AppendLine("            return method();");
                        }
                        else
                        {
                            var paramNames = parameters.Select(p => p.Name ?? $"param{Array.IndexOf(parameters, p)}").ToArray();
                            output.AppendLine($"            return method({string.Join(", ", paramNames)});");
                        }
                        output.AppendLine("        }");
                        output.AppendLine();
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine($"        // Error generating method {method.Name}: {ex.Message}");
                        output.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                output.AppendLine($"        // Error generating methods: {ex.Message}");
                output.AppendLine();
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
                    return $"Mono::Array<{elementCppType}>*";
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
                                return $"Mono::List<{GetCppType(genericArgs[0], currentClass)}>*";
                                // do more stuff
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
                        /*
                    case "GTZone": return "__REMOVE__GTZone";
                    case "UnityLayer": return "__REMOVE__UnityLayer";
                    case "WearablePackedStateSlots": return "__REMOVE__WearablePackedStateSlots";
                        */
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
    }
}
