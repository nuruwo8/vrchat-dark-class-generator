//#define DARK_CLASS_GENERATOR_LOAD_BY_TEXTAREA

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UdonSharp;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.Udon.Editor;

namespace Nuruwo.Tool
{

    /// <summary>
    /// Class to Generate DarkClass and LoadScript
    /// </summary>
    internal class DarkClassGenerator
    {
        /*------------------------------private types----------------------------------*/
        private enum VectorOrColor
        {
            VECTOR,
            COLOR
        }

        /*------------------------------private values----------------------------------*/
        private readonly StringBuilder _stringBuilder;
        private int _indentLevel;

        private readonly string _nameSpace;
        private readonly string _className;
        private readonly List<string> _fieldList;
        private readonly string _indentStringUnit;
        private readonly bool _doGenerateSetMethod;
        private readonly bool _deserializeVectorOrColorAsDataList;
        private readonly bool _isJsonDeserializeMode;
        private readonly Type[] _assemblyTypes;

        private string EnumName => $"{_className}Field";
        private bool HasNameSpace => !string.IsNullOrEmpty(_nameSpace);


        /*------------------------------Constructor------------------------------*/
        public DarkClassGenerator(
            string nameSpace,
            string className,
            List<string> fieldList,
            bool doGenerateSetMethod = true,
            bool deserializeVectorOrColorAsDataList = false,
            bool isJsonDeserializeMode = false,
            string indentStringUnit = "\t")
        {
            _stringBuilder = new StringBuilder();
            _className = className;
            _nameSpace = nameSpace;
            _className = className;
            _fieldList = fieldList;
            _doGenerateSetMethod = doGenerateSetMethod;
            _deserializeVectorOrColorAsDataList = deserializeVectorOrColorAsDataList;
            _isJsonDeserializeMode = isJsonDeserializeMode;
            _indentStringUnit = indentStringUnit;

            //Extract Valid Udon types from all assemblies.
            var typeList = new List<Type>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var udonSharpEditorAssembly = Assembly.Load("UdonSharp.Editor");
            var compilerUdonInterface = udonSharpEditorAssembly.GetType("UdonSharp.Compiler.Udon.CompilerUdonInterface");
            var getUdonTypeName = compilerUdonInterface.GetMethod("GetUdonTypeName", new Type[] { typeof(Type) });
            foreach (var assembly in assemblies)
            {
                typeList.AddRange(assembly.GetTypes().Where(t => t.IsEnum || t.IsSubclassOf(typeof(UdonSharpBehaviour)) || t.IsSubclassOf(typeof(DataList)) || t.IsSubclassOf(typeof(DataDictionary)) || UdonEditorManager.Instance.GetTypeFromTypeString((string)getUdonTypeName.Invoke(null, new object[] { t })) != null));
            }
            _assemblyTypes = typeList.ToArray();
        }

        /*------------------------------Public methods------------------------------*/
        public string GenerateDarkClassCode()
        {
            _stringBuilder.Clear(); // Clear before generate
            _indentLevel = 0;   // Clear indent

            GenerateUsingHeader();
            GenerateNamespaceScope();

            return _stringBuilder.ToString();   //return result
        }

        public static (string nameSpace, string className, List<string> fieldList) LoadParameterFromScript(string text)
        {
            var oneLineText = text.Replace("\r\n", "\n").Replace("\n", " "); //remove line break and join with space

            //namespace
            var nameSpace = ExtractNameSpace(oneLineText);

            //className
            var className = ExtractClassName(oneLineText);

            //Fields
            var fieldList = !string.IsNullOrEmpty(className) ? ExtractFields(oneLineText, className) : new List<string>();

            return (nameSpace, className, fieldList);
        }

        /*-----------------------Generate methods----------------------------*/
        private void GenerateUsingHeader()
        {
            AppendLine($"using UnityEngine;");
            AppendLine($"using VRC.SDK3.Data;");
            AppendLine();
        }

        private void GenerateNamespaceScope()
        {
            if (HasNameSpace)
            {
                AppendLine($"namespace {_nameSpace}");
                AppendLine($"{{");
                Indent();
            }

            GenerateEnum();
            AppendLine();
            GenerateMainClassScope();
            AppendLine();
            GenerateExtensionClassScope();

            if (HasNameSpace)
            {
                Outdent();
                AppendLine($"}}");
            }
        }

        private void GenerateEnum()
        {
            AppendLine($"// Enum for assigning index of field DataTokens");
            AppendLine($"enum {EnumName}");
            AppendLine($"{{");
            Indent();
            foreach (var field in _fieldList)
            {
                var (_, argumentName) = GetTypeAndNameFromField(field);
                if (string.IsNullOrEmpty(argumentName)) { continue; }
                var pArgumentName = ToPascal(argumentName);
                AppendLine($"{pArgumentName},");
            }
            AppendLine();
            AppendLine($"Count");
            Outdent();
            AppendLine($"}}");
        }

        /*-----------------------Main class scope--------------------------------*/
        private void GenerateMainClassScope()
        {
            AppendLine($"public class {_className} : DataList");
            AppendLine($"{{");
            Indent();

            AppendLine("// Constructor");
            if (!_isJsonDeserializeMode)
            {
                // default mode
                GenerateDefaultConstructor();
            }
            else
            {
                //json mode
                GenerateCommentForLoadScriptInJsonMode();
                GenerateJsonConstructor();
            }

            Outdent();
            AppendLine($"}}");
        }

        private void GenerateDefaultConstructor()
        {
            Append($"public static {_className} New(");
            {
                var arguments = new List<string>();
                foreach (var field in _fieldList)
                {
                    var (argumentType, argumentName) = GetTypeAndNameFromField(field);
                    if (string.IsNullOrEmpty(argumentType)) { continue; }
                    arguments.Add($"{argumentType} {argumentName}");
                }
                Append(string.Join(", ", arguments), false);
            }
            AppendLine($")", false);

            AppendLine($"{{");
            Indent();

            //data token 
            GenerateDataTokenAssign();

            Outdent();
            AppendLine($"}}");
        }

        private void GenerateDataTokenAssign()
        {
            //data token 
            AppendLine($"var data = new DataToken[(int){EnumName}.Count];");
            AppendLine();

            foreach (var field in _fieldList)
            {
                var (argumentType, argumentName) = GetTypeAndNameFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                var pArgumentName = ToPascal(argumentName);
                var typeIsReference = CheckDataTokenTypeIsReference(argumentType);
                var arg = typeIsReference ? $"new DataToken({argumentName})" : $"{argumentName}";

                AppendLine($"data[(int){EnumName}.{pArgumentName}] = {arg};");
            }

            AppendLine();
            AppendLine($"return ({_className})new DataList(data);");
        }

        /*-----------------------Json generate in Main class --------------------------------*/
        private void GenerateCommentForLoadScriptInJsonMode()
        {
            AppendLine($"// This comments for loading this script by generator : ");
            Append($"// public static {_className} New(");
            var arguments = new List<string>();
            foreach (var field in _fieldList)
            {
                var (argumentType, argumentName, jsonKey) = GetTypeAndNameAndJsonKeyFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                if (!string.IsNullOrEmpty(jsonKey))
                {
                    arguments.Add($"{argumentType} {argumentName} {jsonKey}");
                }
                else
                {
                    arguments.Add($"{argumentType} {argumentName}");
                }
            }
            Append(string.Join(", ", arguments), false);

            AppendLine($")", false);
        }

        private void GenerateJsonConstructor()
        {
            AppendLine($"public static {_className} New(DataDictionary dic)");
            AppendLine($"{{");
            Indent();

            foreach (var field in _fieldList)
            {
                GenerateJsonFieldDeclaration(field);
            }
            AppendLine();

            //object 
            AppendLine($"// Make DataTokens");
            GenerateDataTokenAssign();

            Outdent();
            AppendLine($"}}");
        }

        private void GenerateJsonFieldDeclaration(string field)
        {
            var (argumentType, argumentName, jsonKey) = GetTypeAndNameAndJsonKeyFromField(field);
            if (string.IsNullOrEmpty(argumentType)) { return; }
            //jsonKey
            jsonKey = string.IsNullOrEmpty(jsonKey) ? argumentName : jsonKey;

            //type of arrays (List is not supported)
            var isArray = Regex.IsMatch(argumentType, @".+\[\s*\]");
            if (isArray)
            {
                GenerateJsonArrayField(argumentType, argumentName, jsonKey);
            }
            else
            {
                //type of defaults
                GenerateJsonCastedTypeAndNameFromField(argumentType, argumentName, isArray, jsonKey);
            }
        }

        private void GenerateJsonArrayField(string argumentType, string argumentName, string jsonKey)
        {
            var pureType = argumentType.Replace("[]", "");

            AppendLine();    //For readability
            AppendLine($"var {argumentName}List = dic[\"{jsonKey}\"].DataList;");
            AppendLine($"var {argumentName}Count = {argumentName}List.Count;");
            AppendLine($"var {argumentName} = new {pureType}[{argumentName}Count];");

            AppendLine($"for (int i = 0; i < {argumentName}Count; i++)");
            AppendLine($"{{");

            GenerateJsonCastedTypeAndNameFromField(pureType, argumentName, true, jsonKey);

            AppendLine($"}}");
        }

        private void GenerateJsonCastedTypeAndNameFromField(string argumentType, string argumentName, bool isArrayElement, string jsonKey)
        {
            //first. process Vector or Color
            switch (argumentType)
            {

                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Quaternion":
                    GenerateJsonVectorOrColorField(argumentType, argumentName, isArrayElement, jsonKey, VectorOrColor.VECTOR);
                    return;
                case "Color":
                case "Color32":
                    GenerateJsonVectorOrColorField(argumentType, argumentName, isArrayElement, jsonKey, VectorOrColor.COLOR);
                    return;
            }

            //second. other process.
            var leftPreString = isArrayElement ? "" : "var ";
            var leftPostString = isArrayElement ? "[i]" : "";

            //make StringBuilder. it should be one line to AppendLine() correctly.
            var sb = new StringBuilder();
            sb.Append($"{leftPreString}{argumentName}{leftPostString} = ");
            var rightString = isArrayElement ? $"{argumentName}List[i]" : $"dic[\"{jsonKey}\"]";

            //check Enum
            var typeIsEnum = CheckTypeIsEnum(argumentType);
            if (typeIsEnum)
            {
                sb.Append($"({argumentType})(int){rightString}.Number;");

                if (isArrayElement) { Indent(); }
                AppendLine(sb.ToString());
                if (isArrayElement) { Outdent(); }
                return;
            }

            //check reference
            var typeIsReference = CheckDataTokenTypeIsReference(argumentType);
            if (typeIsReference)
            {
                sb.Append($"({argumentType}){rightString}.Reference;");

                if (isArrayElement) { Indent(); }
                AppendLine(sb.ToString());
                if (isArrayElement) { Outdent(); }
                return;
            }

            switch (argumentType)
            {
                case "bool":
                    sb.Append($"{rightString}.Boolean;");
                    break;
                case "char":
                    sb.Append($"{rightString}.String[0];");
                    break;
                case "string":
                    sb.Append($"{rightString}.String;");
                    break;
                case "byte":
                case "sbyte":
                case "decimal":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "short":
                case "ushort":
                case "float":
                case "double":
                    //number
                    sb.Append($"({argumentType}){rightString}.Number;");
                    break;
                case "DataList":
                case "DataDictionary":
                    sb.Append($"{rightString}.{argumentType};");
                    break;
                default:
                    //Dark class
                    if (!isArrayElement)
                    {
                        sb.Append($"{argumentType}.New(dic[\"{jsonKey}\"].DataDictionary);");
                    }
                    else
                    {
                        sb.Append($"{argumentType}.New({argumentName}List[i].DataDictionary);");
                    }
                    break;
            }

            if (isArrayElement) { Indent(); }
            AppendLine(sb.ToString());
            if (isArrayElement) { Outdent(); }
            return;
        }

        private void GenerateJsonVectorOrColorField(string argumentType, string argumentName, bool isArrayElement, string jsonKey, VectorOrColor type)
        {
            //prepare strings
            var elementStrings = new string[0];

            switch (type)
            {
                case VectorOrColor.VECTOR:
                    elementStrings = new string[] { "x", "y", "z", "w" };
                    break;
                case VectorOrColor.COLOR:
                    elementStrings = new string[] { "r", "g", "b", "a" };
                    break;
            }

            int elementNum;
            switch (argumentType)
            {
                case "Vector2":
                    elementNum = 2;
                    break;
                case "Vector3":
                    elementNum = 3;
                    break;
                case "Vector4":
                case "Quaternion":
                case "Color":
                case "Color32":
                    elementNum = 4;
                    break;
                default:
                    elementNum = 4;
                    break;
            }

            var cast = string.Empty;
            switch (type)
            {
                case VectorOrColor.VECTOR:
                    cast = "(float)";
                    break;
                case VectorOrColor.COLOR:
                    switch (argumentType)
                    {
                        case "Color":
                            cast = "(float)";
                            break;
                        case "Color32":
                            cast = "(byte)";
                            break;
                        default:
                            break;
                    }
                    break;
            }

            if (isArrayElement) { Indent(); }
            var arrayElement = isArrayElement ? $"[i]" : $"";
            var argNameString = isArrayElement ? $"{argumentName}List[i]" : $"dic[\"{jsonKey}\"]";
            var varRightString = _deserializeVectorOrColorAsDataList ? $"{argNameString}.DataList;" : $"{argNameString}.DataDictionary;";
            var dataName = $"{argumentName}Data";

            // make string
            if (!isArrayElement)
            {
                AppendLine();    //For readability
            }
            AppendLine($"var {dataName} = {varRightString}");

            for (int i = 0; i < elementNum; i++)
            {
                var elementName = argumentName + elementStrings[i].ToUpper();
                var elementTarget = _deserializeVectorOrColorAsDataList ? $"[{i}]" : $"[\"{elementStrings[i]}\"]";
                AppendLine($"var {elementName} = {cast}{dataName}{elementTarget}.Number;");
            }

            //constructor
            var varString = isArrayElement ? $"" : $"var ";
            Append($"{varString}{argumentName}{arrayElement} = new {argumentType}(");

            var arguments = new List<string>();
            for (int i = 0; i < elementNum; i++)
            {
                arguments.Add(argumentName + elementStrings[i].ToUpper());
            }
            Append(string.Join(", ", arguments), false);
            AppendLine($");", false);

            if (isArrayElement) { Outdent(); }
        }


        /*-----------------------Extension class scope--------------------------------*/
        private void GenerateExtensionClassScope()
        {
            AppendLine($"public static class {_className}Ext");
            AppendLine($"{{");
            Indent();

            AppendLine("// Get methods");
            GenerateGetMethods();

            if (_doGenerateSetMethod)
            {
                AppendLine();
                AppendLine("// Set methods");
                GenerateSetMethods();
            }

            Outdent();
            AppendLine($"}}");
        }

        private void GenerateGetMethods()
        {
            foreach (var field in _fieldList)
            {
                var (argumentType, argumentName) = GetTypeAndNameFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                var pArgumentName = ToPascal(argumentName);

                var typeIsReference = CheckDataTokenTypeIsReference(argumentType);
                var dataTokenType = typeIsReference ? $".Reference" : $"";

                AppendLine($"public static {argumentType} {pArgumentName}(this {_className} instance)");
                Indent();
                AppendLine($"=> ({argumentType})instance[(int){EnumName}.{pArgumentName}]{dataTokenType};");
                Outdent();
            }
        }

        private void GenerateSetMethods()
        {
            foreach (var field in _fieldList)
            {
                var (argumentType, argumentName) = GetTypeAndNameFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                var pArgumentName = ToPascal(argumentName);

                var typeIsReference = CheckDataTokenTypeIsReference(argumentType);
                var arg = typeIsReference ? $"new DataToken(arg)" : $"arg";

                AppendLine($"public static void {pArgumentName}(this {_className} instance, {argumentType} arg)");
                Indent();
                AppendLine($"=> instance[(int){EnumName}.{pArgumentName}] = {arg};");
                Outdent();
            }
        }

        /*----------------------Parameters Utilities ----------------------------*/
        private (string, string) GetTypeAndNameFromField(string field)
        {
            var args = Regex.Split(field.Trim(), @"\s+");
            if (args.Length < 2) { return (string.Empty, string.Empty); }
            var argumentType = args[0].Trim();
            var argumentName = args[1].Trim();
            return (argumentType, argumentName);
        }

        private (string, string, string) GetTypeAndNameAndJsonKeyFromField(string field)
        {
            var args = Regex.Split(field.Trim(), @"\s+");
            if (args.Length < 2) { return (string.Empty, string.Empty, string.Empty); }
            var argumentType = args[0].Trim();
            var argumentName = args[1].Trim();
            var jsonKey = string.Empty;
            if (args.Length >= 3)
            {
                jsonKey = args[2].Trim();
            }
            return (argumentType, argumentName, jsonKey);
        }

        private bool CheckDataTokenTypeIsReference(string argumentType)
        {
            var typeIsArray = Regex.IsMatch(argumentType, @"\[\s*\]");
            var typeIsReference = argumentType == "object" || argumentType == "decimal";

            var types = _assemblyTypes.Where(t => t.Name == argumentType);
            var type = types.FirstOrDefault(t => t.IsSubclassOf(typeof(DataList))) ?? types.FirstOrDefault();

            if (type != null)
            {
                if (type.IsSubclassOf(typeof(UnityEngine.Object)) || type.IsEnum || type.IsInterface) typeIsReference = true;
                else if (type != typeof(DataList) && !type.IsSubclassOf(typeof(DataList)) && type != typeof(DataDictionary) && !type.IsSubclassOf(typeof(DataDictionary)))
                {
                    object instance = null;
                    try
                    {
                        instance = Activator.CreateInstance(type);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning(e);
                        typeIsReference = true;
                    }
                    if (instance != null)
                    {
                        var data = CreateDataTokenInstance(type, instance);
                        typeIsReference = data.TokenType == TokenType.Reference;
                    }
                }
            }

            return typeIsArray || typeIsReference;
        }

        private DataToken CreateDataTokenInstance(Type type, object value)
        {
            var dataTokenType = typeof(DataToken);
            var constructor = dataTokenType.GetConstructor(new Type[] { type });

            if (constructor == null)
            {
                return new DataToken(value);
            }

            return (DataToken)constructor.Invoke(new object[] { value });
        }


        private bool CheckTypeIsEnum(string argumentType)
        {
            var typeIsEnum = false;

            var type = _assemblyTypes.FirstOrDefault(t => t.Name == argumentType);
            if (type != null)
            {
                typeIsEnum = type.IsEnum;
            }

            return typeIsEnum;
        }

        /*------------------------------String Utilities---------------------------------*/
        internal void Indent() { _indentLevel++; }

        internal void Outdent() { _indentLevel--; }

        private string IndentString()
        {
            return string.Concat(Enumerable.Repeat(_indentStringUnit, _indentLevel));
        }

        private StringBuilder Append<T>(T value, bool withIndent = true)
        {
            if (withIndent)
            {
                _stringBuilder.Append(IndentString());
            }

            _stringBuilder.Append(value);
            return _stringBuilder;
        }

        private StringBuilder AppendLine(string value, bool withIndent = true)
        {
            if (withIndent)
            {
                _stringBuilder.Append(IndentString());
            }

            _stringBuilder.AppendLine(value);
            return _stringBuilder;
        }

        internal StringBuilder AppendLine(bool withIndent = true)
        {
            if (withIndent)
            {
                _stringBuilder.Append(IndentString());
            }

            _stringBuilder.AppendLine();
            return _stringBuilder;
        }
        private static string ToPascal(string text)
        {
            return Regex.Replace(text.Replace("_", " "), @"\b[a-z]", match => match.Value.ToUpper()).Replace(" ", "");
        }

        /*------------------------------Static Methods---------------------------------*/

        private static string ExtractNameSpace(string oneLineText)
        {
            var matches = Regex.Matches(oneLineText, @"\s*namespace\s+[^\s]+\s+");
            if (matches.Count() <= 0) { return string.Empty; }
            var match = matches[0].Value.Trim();
            if (!string.IsNullOrEmpty(match))
            {
                var words = Regex.Split(match, @"\s+");
                if (words.Length >= 2)
                {
                    return words[1];
                }
            }
            return string.Empty;
        }

        private static string ExtractClassName(string oneLineText)
        {
            var matches = Regex.Matches(oneLineText, @"\s*public\s+class\s+[^\s]+\s+:");
            if (matches.Count() <= 0) { return string.Empty; }
            var match = matches[0].Value.Trim();
            if (!string.IsNullOrEmpty(match))
            {
                var words = Regex.Split(match, @"\s+");
                var classIndex = Array.IndexOf(words, "class");
                if (words.Length >= classIndex + 1)
                {
                    return words[classIndex + 1];
                }
            }
            return string.Empty;
        }

        private static List<string> ExtractFields(string oneLineText, string className)
        {
            var fields = new List<string>();

            var matches = Regex.Matches(oneLineText, @"\s*public\s+static\s+" + className + @"\s+New\s*\([^\)]+\)");
            if (matches.Count() <= 0) { return fields; }
            var match = matches[0].Value.Trim();
            if (!string.IsNullOrEmpty(match))
            {
                var startIndex = match.IndexOf("(");
                var endIndex = match.IndexOf(")");
                var src = match.Substring(startIndex + 1, endIndex - startIndex - 1);
                var words = Regex.Split(src, @"\s*,\s*");
                if (words.Length > 0)
                {
                    fields.AddRange(words);
                }
            }
            return fields;
        }
    }

    /// <summary>
    /// Class to draw EditorWindow
    /// </summary>
    public class DarkClassGeneratorEditorWindow : EditorWindow
    {
        /*------------------------------private values----------------------------------*/
        private bool _loadFoldIsOpen = false;

        private bool _doGenerateSetMethod = true;
        private bool _isJsonDeserializeMode = false;
        private bool _deserializeVectorOrColorAsDataList = false;

        private TextAsset _textAsset;
        private string _prevScriptName = string.Empty;
        private ReorderableList _reorderableFieldList;
        private string _className = string.Empty;
        private string _nameSpace = string.Empty;
        private List<string> _fieldList = new List<string>() { "int value" };
        private string _generatedCode = string.Empty;
        private Vector2 _scrollPositionOnGui = Vector2.zero; //for OnGUI scroll
        private Vector2 _scrollPositionResult = Vector2.zero; //for textArea scroll


#if DARK_CLASS_GENERATOR_LOAD_BY_TEXTAREA
        private Vector2 _scrollPositionScript = Vector2.zero; //for textArea scroll
        private string _scriptText = string.Empty;
#endif

        /*------------------------------Initialize------------------------------*/
        [MenuItem("Tools/Nuruwo/DarkClassGenerator", false, 1)]
        public static void Open()
        {
            var window = GetWindow<DarkClassGeneratorEditorWindow>();
            window.titleContent = new GUIContent("Dark class generator");
        }

        private void OnEnable()
        {
            UpdateReorderableFieldList(_fieldList);
        }

        /*------------------------------UI Look---------------------------------*/
        private void OnGUI()
        {
            _scrollPositionOnGui = EditorGUILayout.BeginScrollView(_scrollPositionOnGui);

            EditorGUILayout.Space(10);

            DrawLoadFromScript();
            EditorGUILayout.Space(10);

            DrawClassParameters();
            EditorGUILayout.Space(5);

            DrawOptions();
            EditorGUILayout.Space(5);

            DrawGenerateButton();
            EditorGUILayout.Space(10);

            DrawTextArea();

            EditorGUILayout.EndScrollView();
        }

        /*------------------------------Draw methods---------------------------------*/
        private void DrawLoadFromScript()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            _loadFoldIsOpen = EditorGUILayout.Foldout(_loadFoldIsOpen, "Load class parameters from script");
            if (_loadFoldIsOpen)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.Space(10);
                EditorGUILayout.BeginHorizontal();
                _textAsset = (TextAsset)EditorGUILayout.ObjectField("Script to load", _textAsset, typeof(TextAsset), false);
                var scriptName = _textAsset != null ? _textAsset.name : string.Empty;
                if (!string.IsNullOrEmpty(scriptName) && _prevScriptName != scriptName)
                {
                    _prevScriptName = scriptName;   //script is changed
                    (_nameSpace, _className, _fieldList) = DarkClassGenerator.LoadParameterFromScript(_textAsset.text);
                    UpdateReorderableFieldList(_fieldList);
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(scriptName)))
                {
                    if (GUILayout.Button("Reload script"))
                    {
                        (_nameSpace, _className, _fieldList) = DarkClassGenerator.LoadParameterFromScript(_textAsset.text);
                        UpdateReorderableFieldList(_fieldList);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(10);

#if DARK_CLASS_GENERATOR_LOAD_BY_TEXTAREA
                EditorGUILayout.Space(10);
                GUILayout.Label("Load parameters from text", EditorStyles.boldLabel);
                EditorGUILayout.Space(10);
                _scrollPositionScript = EditorGUILayout.BeginScrollView(_scrollPositionScript);
                var textAreaStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = true
                };
                _scriptText = GUILayout.TextArea(_scriptText, textAreaStyle);
                EditorGUILayout.EndScrollView();
                EditorGUILayout.Space(10);
                if (GUILayout.Button("Load script"))
                {
                    (_nameSpace, _className, _fieldList) = DarkClassGenerator.LoadParameterFromScript(_scriptText);
                    UpdateReorderableFieldList(_fieldList);
                }
                EditorGUILayout.Space(10);
#endif
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawClassParameters()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Class parameters", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            //namespace
            EditorGUILayout.Space(10);
            _nameSpace = EditorGUILayout.TextField("namespace (optional)", _nameSpace);
            EditorGUILayout.Space(10);

            //Class name
            EditorGUILayout.Space(10);
            _className = EditorGUILayout.TextField("Class name", _className);
            EditorGUILayout.Space(10);

            //Augment list
            _reorderableFieldList.DoLayoutList();
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private void DrawOptions()
        {
            EditorGUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Options", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.Space(5);
            _doGenerateSetMethod = EditorGUILayout.ToggleLeft(" Generate Set methods", _doGenerateSetMethod);
            EditorGUILayout.Space(5);
            _isJsonDeserializeMode = EditorGUILayout.ToggleLeft(" JSON deserialize mode", _isJsonDeserializeMode);
            EditorGUILayout.Space(10);
            _deserializeVectorOrColorAsDataList = EditorGUILayout.ToggleLeft(" In JSON mode, Vector or Color as DataList", _deserializeVectorOrColorAsDataList);
            EditorGUILayout.Space(5);

            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private void DrawGenerateButton()
        {
            var canGenerate = !string.IsNullOrEmpty(_className) && _fieldList.Count > 0 && !string.IsNullOrEmpty(_fieldList[0]);

            var result = string.Empty;

            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                //Generate 
                if (GUILayout.Button("Generate DarkClass"))
                {
                    var generator = new DarkClassGenerator(
                        _nameSpace,
                        _className,
                        _fieldList,
                        _doGenerateSetMethod,
                        _deserializeVectorOrColorAsDataList,
                        _isJsonDeserializeMode,
                        "\t"
                    );
                    _generatedCode = generator.GenerateDarkClassCode();
                    //clip board
                    EditorGUIUtility.systemCopyBuffer = _generatedCode;
                    result = "Code is generated and Copied to clipboard.";
                }
            }
            GUILayout.Label(result);
        }

        private void DrawTextArea()
        {
            _scrollPositionResult = EditorGUILayout.BeginScrollView(_scrollPositionResult);
            var textAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true
            };
            GUILayout.TextArea(_generatedCode, textAreaStyle);
            EditorGUILayout.EndScrollView();
        }

        /*------------------------------Reorderable List---------------------------------*/
        private void UpdateReorderableFieldList(List<string> list)
        {
            _fieldList = list;
            _reorderableFieldList = new ReorderableList(
              elements: _fieldList,
              elementType: typeof(string),
              draggable: true,
              displayHeader: true,
              displayAddButton: true,
              displayRemoveButton: true
            );
            _reorderableFieldList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Field List");
            _reorderableFieldList.elementHeightCallback = index => 20;
            _reorderableFieldList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                _fieldList[index] = EditorGUI.TextField(rect, "  Field (type and name) " + index, _fieldList[index]);
            };
        }
    }
}
#endif