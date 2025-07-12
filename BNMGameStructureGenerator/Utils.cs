using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using dnlib.DotNet;

namespace BNMGameStructureGenerator
{
    public static class Utils
    {
        public static readonly HashSet<string> ReservedTypeNames = new HashSet<string>
        {
            "RenderMode",
            "Method",
            "IEnumerator",
            "IEnumerable",
            "ICollection",
            "IList",
            "IDictionary",
            "IComparer",
            "IEqualityComparer",
            "StringComparison",
            "BehaviorRegisterDelegate",
            "BoingReactorField",
            "StringComparison"
        };

        public static string CleanTypeName(string typeName)
        {
            if (typeName.Contains("`"))
            {
                return typeName.Split('`')[0];
            }
            return typeName;
        }
        public static string GetBaseClass<T>(T type, T currentClass = default(T))
        {
            object baseType = GetBaseType(type);
            if (baseType == null || GetBaseTypeFullName(type) == "System.Object" || GetBaseTypeFullName(type) == "System.Object") { return ""; }

            string baseTypeName = CleanTypeName(GetBaseTypeName(type));

            if (baseType != null && baseType.Equals(type))
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

            if (GetBaseTypeName(type).Contains("`"))
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
        public static string GetCppType<T>(T type, object currentClass = default(object))
        {
            try
            {
                if (IsNullableType(type))
                {
                    type = GetNullableUnderlyingType(type);
                }

                string fullName = GetTypeFullName(type);
                if (fullName != null && 
                    (fullName.Contains("System.Collections") || 
                     fullName.Contains("IEnumerator") ||
                     fullName.Contains("IEnumerable") ||
                     fullName.Contains("ICollection") ||
                     fullName.Contains("IList") ||
                     fullName.Contains("IDictionary")))
                {
                    return $"__REMOVE__{GetTypeName(type)}";
                }

                string typeNamespace = GetTypeNamespace(type);
                if (!string.IsNullOrEmpty(typeNamespace) && !typeNamespace.StartsWith("System") && !typeNamespace.StartsWith("UnityEngine"))
                {
                    return $"__REMOVE__{GetTypeName(type)}";
                }

                if (IsArrayType(type))
                {
                    object elementType = GetArrayElementType(type);
                    string elementCppType = GetCppType(elementType, currentClass);
                    return $"BNM::Structures::Mono::Array<{elementCppType}>*";
                }

                if (IsGenericType(type))
                {
                    try
                    {
                        string genericTypeName = GetGenericTypeName(type);
                        var genericArgs = GetGenericArguments(type);

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
                        Console.WriteLine($"Warning: Could not process generic type {GetTypeName(type)}: {ex.Message}");
                        return "void*";
                    }
                }

                if (IsEnumType(type))
                {
                    string enumTypeNamespace = GetTypeNamespace(type);
                    if (!string.IsNullOrEmpty(enumTypeNamespace) && !enumTypeNamespace.StartsWith("System") && !enumTypeNamespace.StartsWith("UnityEngine"))
                    {
                        return $"__REMOVE__{CleanTypeName(GetTypeName(type))}";
                    }

                    if (string.IsNullOrEmpty(enumTypeNamespace) && !Program.definedTypes.Contains(CleanTypeName(GetTypeName(type))))
                    {
                        return $"__REMOVE__{CleanTypeName(GetTypeName(type))}";
                    }

                    if (!string.IsNullOrEmpty(enumTypeNamespace) && !enumTypeNamespace.StartsWith("System"))
                    {
                        if (Program.definedTypes.Contains(CleanTypeName(GetTypeName(type))))
                        {
                            return $"{enumTypeNamespace.Replace(".", "::")}::{CleanTypeName(GetTypeName(type))}";
                        }
                        else
                        {
                            return "int";
                        }
                    }
                    return CleanTypeName(GetTypeName(type));
                }

                switch (fullName)
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
                    case "System.StringComparison": return "int";
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

                switch (CleanTypeName(GetTypeName(type)))
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
                    case "StringComparison": return "int";
                    default:
                        return $"__REMOVE__{CleanTypeName(GetTypeName(type))}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Error processing type {GetTypeName(type)}: {ex.Message}");
                return "void*";
            }
        }
        public static string ToPascalCase(string input)
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
        public static string ToCamelCase(string input)
        {
            if (string.IsNullOrEmpty(input)) { return input; }

            string pascalCase = ToPascalCase(input);
            if (pascalCase.Length > 0)
            {
                return char.ToLower(pascalCase[0]) + pascalCase.Substring(1);
            }

            return pascalCase;
        }
        public static bool StartsWithNumber(string str)
        {
            if (string.IsNullOrEmpty(str)) return false;
            return char.IsDigit(str[0]);
        }
        public static bool IsKeyword(string str)
        {
            string[] keywords = new string[] {
                "", "alignas", "alignof", "and", "and_eq", "asm", "atomic_cancel", "atomic_commit", "atomic_noexcept",
                "auto", "bitand", "bitor", "bool", "break", "case", "catch", "char", "char8_t", "char16_t", "char32_t",
                "class", "compl", "concept", "const", "consteval", "constexpr", "constinit", "const_cast", "continue",
                "contract_assert", "co_await", "co_return", "co_yield", "decltype", "default", "delete", "do", "double",
                "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto",
                "if", "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq", "nullptr",
                "operator", "or", "or_eq", "public", "protected", "public", "reflexpr", "register", "reinterpret_cast",
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
        public static string[] MakeValidParams(string[] paramNames)
        {
            var results = new List<string>();
            var seen = new Dictionary<string, int>();

            foreach (var param in paramNames)
            {
                string cparam = param;
                
                if (IsKeyword(cparam) || cparam == "unsigned" || cparam == "template" || cparam == "Method")
                {
                    cparam = $"_{cparam}";
                }
                
                if (seen.ContainsKey(cparam))
                {
                    seen[cparam]++;
                    cparam = $"{cparam}_{seen[cparam]}";
                }
                else
                {
                    seen[cparam] = 0;
                }

                results.Add(cparam);
            }

            return results.ToArray();
        }
        public static string FormatInvalidName(string className)
        {
            string str = className.Trim()
                .Replace("<", "$")
                .Replace(">", "$")
                .Replace("|", "$")
                .Replace("-", "$")
                .Replace("`", "$")
                .Replace("=", "$")
                .Replace("@", "$")
                .Replace(".", "_")
                .Replace(":", "_")
                .Replace(" ", "_")
                .Trim();

            if (string.IsNullOrEmpty(str)) { return "_"; }
            if (StartsWithNumber(str)) { str = "_" + str; }
            if (IsKeyword(str)) { str = "$" + str; }

            return str;
        }
        public static string GetEnumUnderlyingType<T>(T enumType)
        {
            try
            {
                if (enumType is Type t)
                {
                    Type underlyingType = Enum.GetUnderlyingType(t);
                    if (underlyingType == typeof(int)) return "int";
                    if (underlyingType == typeof(uint)) return "BNM::Types::uint";
                    if (underlyingType == typeof(long)) return "int64_t";
                    if (underlyingType == typeof(ulong)) return "BNM::Types::ulong";
                    if (underlyingType == typeof(short)) return "short";
                    if (underlyingType == typeof(ushort)) return "BNM::Types::ushort";
                    if (underlyingType == typeof(byte)) return "BNM::Types::byte";
                    if (underlyingType == typeof(sbyte)) return "BNM::Types::sbyte";
                    return "int";
                }
                else if (enumType is TypeDef td)
                {
                    var enumFields = td.Fields.Where(f => f.IsStatic && f.IsLiteral).ToList();
                    if (enumFields.Count > 0)
                    {
                        var firstField = enumFields.First();
                        if (firstField.Constant?.Value != null)
                        {
                            var value = firstField.Constant.Value;
                            if (value is int) return "int";
                            if (value is uint) return "BNM::Types::uint";
                            if (value is long) return "int64_t";
                            if (value is ulong) return "BNM::Types::ulong";
                            if (value is short) return "short";
                            if (value is ushort) return "BNM::Types::ushort";
                            if (value is byte) return "BNM::Types::byte";
                            if (value is sbyte) return "BNM::Types::sbyte";
                        }
                    }
                    return "int";
                }
                return "int";
            }
            catch
            {
                return "int";
            }
        }
        public static string SafeGetNamespace<T>(T t)
        {
            try
            {
                if (t is Type type) return type.Namespace ?? "Global";
                if (t is TypeDef td) return td.Namespace?.ToString() ?? "Global";
                return "Global";
            }
            catch
            {
                return null;
            }
        }
        public static object GetBaseType<T>(T type)
        {
            if (type is Type t) return t.BaseType;
            if (type is TypeDef td) return td.BaseType;
            return null;
        }
        public static string GetBaseTypeName<T>(T type)
        {
            if (type is Type t) return t.BaseType?.Name;
            if (type is TypeDef td) return td.BaseType?.Name;
            return null;
        }
        public static string GetBaseTypeFullName<T>(T type)
        {
            if (type is Type t) return t.BaseType?.FullName;
            if (type is TypeDef td) return td.BaseType?.FullName;
            return null;
        }
        public static bool IsNullableType<T>(T type)
        {
            if (type is Type t) return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
            if (type is TypeSig ts) return ts is GenericInstSig genericSig && ts.FullName?.ToString().Contains("System.Nullable") == true;
            return false;
        }
        public static T GetNullableUnderlyingType<T>(T type)
        {
            if (type is Type t) return (T)(object)Nullable.GetUnderlyingType(t);
            if (type is GenericInstSig genericSig) return (T)(object)genericSig.GenericArguments[0];
            return type;
        }
        public static string GetTypeFullName<T>(T type)
        {
            if (type is Type t) return t.FullName;
            if (type is TypeSig ts) return ts.FullName?.ToString();
            return null;
        }
        public static string GetTypeName<T>(T type)
        {
            if (type is Type t) return t.Name;
            if (type is TypeDef td) return td.Name.ToString();
            if (type is TypeSig ts) return ts.TypeName.ToString();
            return type.ToString();
        }
        public static string GetTypeNamespace<T>(T type)
        {
            if (type is Type t) return t.Namespace;
            if (type is TypeSig ts) return ts.ToTypeDefOrRef()?.Namespace?.ToString();
            return null;
        }
        public static bool IsArrayType<T>(T type)
        {
            if (type is Type t) return t.IsArray;
            if (type is TypeSig ts) return ts is SZArraySig || ts is ArraySig;
            return false;
        }
        public static object GetArrayElementType<T>(T type)
        {
            if (type is Type t) return t.GetElementType();
            if (type is SZArraySig szArray) return szArray.Next;
            if (type is ArraySig arrArray) return arrArray.Next;
            return null;
        }
        public static bool IsGenericType<T>(T type)
        {
            if (type is Type t) return t.IsGenericType;
            if (type is TypeSig ts) return ts is GenericInstSig;
            return false;
        }
        public static string GetGenericTypeName<T>(T type)
        {
            if (type is Type t) return t.Name.Split('`')[0];
            if (type is TypeSig ts) return ts.TypeName.ToString().Split('`')[0];
            return "";
        }
        public static object[] GetGenericArguments<T>(T type)
        {
            if (type is Type t) return t.GetGenericArguments();
            if (type is GenericInstSig genericSig) return genericSig.GenericArguments.ToArray();
            return new object[0];
        }
        public static bool IsEnumType<T>(T type)
        {
            if (type is Type t) return t.IsEnum;
            if (type is TypeSig ts) return ts.IsValueType && ts.FullName?.ToString().Contains("System.Enum") == true;
            return false;
        }
        public static string GetFullName<T>(T type)
        {
            if (type is Type t) return t.FullName ?? t.Name;
            if (type is TypeDef td) return td.FullName ?? td.Name;
            return type.ToString();
        }
        public static bool IsEnum<T>(T type)
        {
            if (type is Type t) return t.IsEnum;
            if (type is TypeDef td) return td.IsEnum;
            return false;
        }
        public static string GetBaseTypeNamespace<T>(T type)
        {
            if (type is Type t) return t.BaseType?.Namespace;
            if (type is TypeDef td) return td.BaseType?.Namespace?.ToString();
            return null;
        }
        public static string GetNamespace<T>(T type)
        {
            if (type is Type t) return t.Namespace;
            if (type is TypeDef td) return td.Namespace?.ToString();
            return null;
        }
        public static IEnumerable<object> GetFields<T>(T type)
        {
            if (type is Type t) return t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (type is TypeDef td) return td.Fields;
            return Enumerable.Empty<object>();
        }
        public static string GetFieldName(object field)
        {
            if (field is FieldInfo fi) return fi.Name;
            if (field is FieldDef fd) return fd.Name.ToString();
            return "";
        }
        public static bool IsLiteral(object field)
        {
            if (field is FieldInfo fi) return fi.IsLiteral;
            if (field is FieldDef fd) return fd.IsLiteral;
            return false;
        }
        public static Array GetEnumValues<T>(T type)
        {
            if (type is Type t) return Enum.GetValues(t);
            if (type is TypeDef td)
            {
                var enumFields = td.Fields.Where(f => f.IsStatic && f.IsLiteral).ToList();
                var values = new List<object>();
                foreach (var field in enumFields)
                {
                    values.Add(field.Constant?.Value ?? 0);
                }
                return values.ToArray();
            }
            return new object[0];
        }
        public static string[] GetEnumNames<T>(T type)
        {
            if (type is Type t) return Enum.GetNames(t);
            if (type is TypeDef td)
            {
                var enumFields = td.Fields.Where(f => f.IsStatic && f.IsLiteral).ToList();
                return enumFields.Select(f => f.Name.ToString()).ToArray();
            }
            return new string[0];
        }
        public static object GetFieldType(object field)
        {
            if (field is FieldInfo fi) return fi.FieldType;
            if (field is FieldDef fd) return fd.FieldType;
            return null;
        }
        public static bool IsNullableType(object type)
        {
            if (type is Type t) return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
            if (type is TypeSig ts) return ts is GenericInstSig genericSig && ts.FullName.Contains("System.Nullable");
            return false;
        }
        public static object GetNullableUnderlyingType(object type)
        {
            if (type is Type t) return Nullable.GetUnderlyingType(t);
            if (type is GenericInstSig genericSig) return genericSig.GenericArguments[0];
            return type;
        }
        public static bool IsClassType<T>(T type)
        {
            if (type is Type t) return t.IsClass;
            if (type is TypeDef td) return td.IsClass;
            if (type is TypeSig ts) return ts.ToTypeDefOrRef()?.ResolveTypeDef()?.IsClass ?? false;
            return false;
        }
        public static bool IsStringType(object type)
        {
            if (type is Type t) return t == typeof(string);
            if (type is TypeSig ts) return ts.FullName == "System.String";
            return false;
        }
        public static string GetTypeNamespace(object type)
        {
            if (type is Type t) return t.Namespace;
            if (type is TypeSig ts) return ts.ToTypeDefOrRef()?.Namespace?.ToString();
            return null;
        }
        public static bool IsStatic(object field)
        {
            if (field is FieldInfo fi) return fi.IsStatic;
            if (field is FieldDef fd) return fd.IsStatic;
            return false;
        }
        public static bool IsInitOnly(object field)
        {
            if (field is FieldInfo fi) return fi.IsInitOnly;
            if (field is FieldDef fd) return fd.IsInitOnly;
            return false;
        }
        public static string FormatTypeNameForStruct(string typeName, string namespaceName)
        {
            string actualTypeName = typeName;
            if (typeName.Contains("."))
            {
                actualTypeName = typeName.Split('.').Last();
            }
            
            string cleanName = CleanTypeName(actualTypeName);
            
            string formattedName = cleanName.Trim()
                .Replace("<", "$")
                .Replace(">", "$")
                .Replace("|", "$")
                .Replace("-", "$")
                .Replace("`", "$")
                .Replace("=", "$")
                .Replace("@", "$")
                .Replace(":", "_")
                .Replace(" ", "_")
                .Trim();

            if (string.IsNullOrEmpty(formattedName)) { return "_"; }
            if (StartsWithNumber(formattedName)) { formattedName = "_" + formattedName; }
            if (IsKeyword(formattedName)) { formattedName = "$" + formattedName; }

            return formattedName;
        }
    }
} 