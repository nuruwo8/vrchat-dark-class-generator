#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Nuruwo.Tool
{
    public class DarkClassGeneratorEditorWindow : EditorWindow
    {
        /*------------------------------private values----------------------------------*/
        private bool _loadFoldIsOpen = false;
        private bool _enumFoldIsOpen = false;

        private bool _generateSetMethod = true;
        private bool _jsonDeserializeMode = false;
        private bool _vectorOrColorAsDataList = false;

        private TextAsset _textAsset;
        private string _prevScriptName = string.Empty;
        private ReorderableList _reorderableFieldList;
        private ReorderableList _reorderableEnumList;
        private string _className = string.Empty;
        private string _nameSpace = string.Empty;
        private List<string> _fieldList = new List<string>() { "int value" };
        private List<string> _enumList = new List<string>() { "UserEnum" };
        private string _result = string.Empty;
        private string _generatedCode = string.Empty;
        private Vector2 _scrollPosition = Vector2.zero; //for textArea scroll
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
            UpdateReorderableEnumList(_enumList);
        }

        /*------------------------------UI Look---------------------------------*/

        private void OnGUI()
        {
            //load from script
            EditorGUILayout.Space(10);
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
                    LoadScript(_textAsset.text);
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(scriptName)))
                {
                    if (GUILayout.Button("Reload script"))
                    {
                        LoadScript(_textAsset.text);
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(10);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);

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
            EditorGUILayout.Space(5);

            //options
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("Options");
                EditorGUI.indentLevel++;
                EditorGUILayout.Space(5);
                _generateSetMethod = EditorGUILayout.ToggleLeft(" Generate Set methods", _generateSetMethod);
                EditorGUILayout.Space(5);
                _jsonDeserializeMode = EditorGUILayout.ToggleLeft(" JSON deserialize mode (Experimental)", _jsonDeserializeMode);
                EditorGUILayout.Space(5);
                EditorGUILayout.Space(5);
                _vectorOrColorAsDataList = EditorGUILayout.ToggleLeft(" Vector or Color as DataList", _vectorOrColorAsDataList);
                EditorGUILayout.Space(5);

                //custom enum
                _enumFoldIsOpen = EditorGUILayout.Foldout(_enumFoldIsOpen, "Set user enum list");
                if (_enumFoldIsOpen)
                {
                    //Augment list
                    _reorderableEnumList.DoLayoutList();
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(5);
            }

            //Generate button
            var canGenerate = !string.IsNullOrEmpty(_className) && _fieldList.Count > 0 && !string.IsNullOrEmpty(_fieldList[0]);
            using (new EditorGUI.DisabledScope(!canGenerate))
            {
                //Generate 
                if (GUILayout.Button("Generate DarkClass"))
                {
                    _generatedCode = GenerateDarkClass(_generateSetMethod);
                    //clip board
                    EditorGUIUtility.systemCopyBuffer = _generatedCode;
                    _result = "Code is generated and Copied to clipboard.";
                }
            }
            EditorGUILayout.Space(10);

            //textArea
            GUILayout.Label(_result);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            var textAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = true
            };
            GUILayout.TextArea(_generatedCode, textAreaStyle);
            EditorGUILayout.EndScrollView();
        }

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

        private void UpdateReorderableEnumList(List<string> list)
        {
            _enumList = list;
            _reorderableEnumList = new ReorderableList(
              elements: _enumList,
              elementType: typeof(string),
              draggable: true,
              displayHeader: true,
              displayAddButton: true,
              displayRemoveButton: true
            );
            _reorderableEnumList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Enum List");
            _reorderableEnumList.elementHeightCallback = index => 20;
            _reorderableEnumList.drawElementCallback = (rect, index, isActive, isFocused) =>
            {
                _enumList[index] = EditorGUI.TextField(rect, "  Enum type " + index, _enumList[index]);
            };
        }
        /*------------------------------Generate DarkClass-----------------------------*/

        private string GenerateDarkClass(bool generateSetMethod)
        {
            var sb = new StringBuilder();
            var pClassName = ToPascal(_className);
            var enumName = pClassName + "Field";

            sb.Append(GenerateHeaderWithNameSpace(_nameSpace));
            var nameSpaceIsExist = !string.IsNullOrEmpty(_nameSpace);
            var nst = nameSpaceIsExist ? 1 : 0; //namespace tab number

            //enum
            sb.Append(AddTabLines("// Enum for assigning index of field DataTokens\r\n", nst));
            var enumString = GenerateEnum(enumName);
            sb.Append(AddTabLines(enumString, nst));
            sb.AppendLine();

            //main class 
            var classHeaderString = GenerateClassHeader(pClassName);
            sb.Append(AddTabLines(classHeaderString, nst));
            sb.Append(AddTabLines("// Constructor\r\n", nst + 1));
            if (!_jsonDeserializeMode)
            {
                // default mode
                var mainClassString = GenerateDefaultConstructor(pClassName, enumName, _fieldList);
                sb.Append(AddTabLines(mainClassString, nst + 1));
                sb.Append(GenerateClassFooter(nameSpaceIsExist));
                sb.AppendLine();
            }
            else
            {
                //json mode
                var commentForLoadScript = GenerateCommentForLoadScript(pClassName, _fieldList);
                sb.Append(AddTabLines(commentForLoadScript, nst + 1));
                var mainClassString = GenerateJsonConstructor(pClassName, enumName, _fieldList);
                sb.Append(AddTabLines(mainClassString, nst + 1));
                sb.Append(GenerateClassFooter(nameSpaceIsExist));
                sb.AppendLine();
            }

            //extension class
            var extensionClassHeaderString = GenerateExtensionClassHeader(pClassName);
            sb.Append(AddTabLines(extensionClassHeaderString, nst));
            sb.Append(AddTabLines("// Get methods\r\n", nst + 1));
            var getMethodsString = GenerateGetMethods(pClassName, enumName, _fieldList);
            sb.Append(AddTabLines(getMethodsString, nst + 1));
            if (generateSetMethod)
            {
                sb.AppendLine();
                sb.Append(AddTabLines("// Set methods\r\n", nst + 1));
                var setMethodsString = GenerateSetMethods(pClassName, enumName, _fieldList);
                sb.Append(AddTabLines(setMethodsString, nst + 1));
            }
            sb.Append(GenerateFooter(nameSpaceIsExist));

            //result
            return sb.ToString();
        }

        /*------------------------------Load script-----------------------------*/
        private string GenerateCommentForLoadScript(string pClassName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// This comments for loading this script by generator : ");
            sb.Append($"// public static {pClassName} New(");
            foreach (var field in fieldList)
            {
                var (argumentType, argumentName, _, jsonKey) = GetTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                if (!string.IsNullOrEmpty(jsonKey))
                {
                    sb.Append($"{argumentType} {argumentName} {jsonKey}, ");
                }
                else
                {
                    sb.Append($"{argumentType} {argumentName}, ");
                }
            }
            sb.Remove(sb.Length - 2, 2);    //remove last comma and space
            sb.AppendLine($")");
            return sb.ToString();
        }

        /*------------------------------Generate Default Part Code-----------------------------*/
        private string GenerateClassFooter(bool nameSpaceIsExist)
        {
            return nameSpaceIsExist ? "\t}\r\n" : "}\r\n";
        }

        private string AddTabLines(string srcText, int tabNum)
        {
            var sb = new StringBuilder();
            var texts = srcText.Replace("\r\n", "\n").Split("\n");
            for (int i = 0; i < texts.Length - 1; i++)  //last line is empty
            {
                var tabText = String.Copy(texts[i]);
                for (int j = 0; j < tabNum; j++)
                {
                    tabText = tabText.Insert(0, "\t");
                }
                sb.AppendLine(tabText);
            }
            return sb.ToString();
        }

        private string GenerateHeaderWithNameSpace(string nameSpace)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"using UnityEngine;");
            sb.AppendLine($"using VRC.SDK3.Data;");
            sb.AppendLine("");
            if (!string.IsNullOrEmpty(nameSpace))
            {
                sb.AppendLine($"namespace {nameSpace}");
                sb.AppendLine($"{{");
            }
            return sb.ToString();
        }

        private string GenerateEnum(string enumName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"enum {enumName}");
            sb.AppendLine($"{{");
            foreach (var field in _fieldList)
            {
                var (argumentType, _, pArgumentName, _) = GetTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                sb.AppendLine($"\t{pArgumentName},");
            }
            sb.AppendLine($"");
            sb.AppendLine($"\tCount");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private string GenerateClassHeader(string pClassName)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"[AddComponentMenu(\"\")]");
            sb.AppendLine($"public class {pClassName} : DataList");
            sb.AppendLine($"{{");
            return sb.ToString();
        }

        private string GenerateDefaultConstructor(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            sb.Append($"public static {pClassName} New(");
            foreach (var field in fieldList)
            {
                var (argumentType, argumentName, _, _) = GetTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }
                sb.Append($"{argumentType} {argumentName}, ");
            }
            sb.Remove(sb.Length - 2, 2);    //remove last comma and space
            sb.AppendLine($")");
            sb.AppendLine($"{{");

            //data token 
            sb.Append(GenerateDataTokenAssign(pClassName, enumName, fieldList));
            return sb.ToString();
        }

        private string GenerateExtensionClassHeader(string pClassName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"public static class {pClassName}Ext");
            sb.AppendLine($"{{");
            return sb.ToString();
        }

        private string GenerateGetMethods(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            foreach (var field in fieldList)
            {
                var (argumentType, _, pArgumentName, _) = GetTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }

                var typeIsReference = CheckDataTokenTypeIsReference(argumentType);
                var dataTokenType = typeIsReference ? $".Reference" : $"";

                sb.AppendLine($"public static {argumentType} {pArgumentName}(this {pClassName} instance)");
                sb.AppendLine($"\t=> ({argumentType})instance[(int){enumName}.{pArgumentName}]{dataTokenType};");
            }
            return sb.ToString();
        }

        private string GenerateSetMethods(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            foreach (var field in fieldList)
            {
                var (argumentType, _, pArgumentName, _) = GetTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }

                var typeIsReference = CheckDataTokenTypeIsReference(argumentType);
                var arg = typeIsReference ? $"new DataToken(arg)" : $"arg";

                sb.AppendLine($"public static void {pArgumentName}(this {pClassName} instance, {argumentType} arg)");
                sb.AppendLine($"\t=> instance[(int){enumName}.{pArgumentName}] = {arg};");
            }
            return sb.ToString();
        }

        private string GenerateFooter(bool nameSpaceIsExist)
        {
            var sb = new StringBuilder();
            if (nameSpaceIsExist)
            {
                sb.AppendLine("\t}");
            }
            sb.Append("}");
            return sb.ToString();
        }

        private (string, string, string, string) GetTypeAndNamesFromField(string field)
        {
            var args = Regex.Split(field.Trim(), @"\s+");
            if (args.Length < 2) { return (string.Empty, string.Empty, string.Empty, string.Empty); }
            var argumentType = args[0].Trim();
            var argumentName = args[1].Trim();
            var pArgumentName = ToPascal(argumentName);
            var jsonKey = string.Empty;
            if (args.Length >= 3)
            {
                jsonKey = args[2].Trim();
            }
            return (argumentType, argumentName, pArgumentName, jsonKey);
        }

        private bool CheckDataTokenTypeIsReference(string argumentType)
        {
            var typeIsEnum = _enumList.ToArray().Contains(argumentType);
            if (typeIsEnum)
            {
                //user enum type is Reference
                return true;
            }

            if (Regex.IsMatch(argumentType, @"\[\s*\]"))
            {
                //array is reference
                return true;
            }

            //You can add more types
            var referenceCastTypes = new string[]{
                "TrackingDataType",
                "HumanBodyBones",
                "TextureWrapMode",
                "GraphicsFormat",
                "TextureFormat",
                "Vector2",
                "Vector3",
                "Vector4",
                "Quaternion",
                "Color",
                "Color32",
            };

            return Array.IndexOf(referenceCastTypes, argumentType) != -1;   //if type is find in array. return true;
        }

        /*------------------------------Generate JSON Part Code-----------------------------*/

        private string GenerateJsonConstructor(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"public static {pClassName} New(DataDictionary dic)");
            sb.AppendLine($"{{");
            foreach (var field in fieldList)
            {
                var jsonField = JsonFieldDeclaration(field);
                sb.AppendLine(jsonField);
            }
            sb.AppendLine();

            //object 
            sb.Append(GenerateDataTokenAssign(pClassName, enumName, fieldList));
            return sb.ToString();
        }

        private string JsonFieldDeclaration(string field)
        {
            var (argumentType, argumentName, _, jsonKey) = GetTypeAndNamesFromField(field);
            if (string.IsNullOrEmpty(argumentType)) { return string.Empty; }
            //jsonKey
            jsonKey = string.IsNullOrEmpty(jsonKey) ? argumentName : jsonKey;

            //type of arrays (List is not supported)
            var isArray = Regex.IsMatch(argumentType, @".+\[\s*\]");
            if (isArray)
            {
                return GenerateJsonArrayField(argumentType, argumentName, jsonKey);
            }

            //type of defaults
            return GetJsonCastedTypeAndNameFromField(argumentType, argumentName, isArray, jsonKey);

        }

        private string GenerateJsonArrayField(string argumentType, string argumentName, string jsonKey)
        {
            var sb = new StringBuilder();

            var pureType = argumentType.Replace("[]", "");

            sb.AppendLine();    //For readability
            sb.AppendLine($"\tvar {argumentName}List = dic[\"{jsonKey}\"].DataList;");
            sb.AppendLine($"\tvar {argumentName}Count = {argumentName}List.Count;");
            sb.AppendLine($"\tvar {argumentName} = new {pureType}[{argumentName}Count];");

            sb.AppendLine($"\tfor (int i = 0; i < {argumentName}Count; i++)");
            sb.AppendLine($"\t{{");

            sb.AppendLine(GetJsonCastedTypeAndNameFromField(pureType, argumentName, true, jsonKey));

            sb.Append($"\t}}");

            return sb.ToString();
        }

        private string GetJsonCastedTypeAndNameFromField(string argumentType, string argumentName, bool isArrayElement, string jsonKey)
        {
            //first. process Vector or Color
            switch (argumentType)
            {

                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Quaternion":
                    return GenerateJsonVectorField(argumentType, argumentName, isArrayElement, jsonKey);
                case "Color":
                case "Color32":
                    return GenerateJsonColorField(argumentType, argumentName, isArrayElement, jsonKey);
            }

            //second. other process.
            var sb = new StringBuilder();

            var leftPreString = isArrayElement ? "\t\t" : "\tvar ";
            var leftPostString = isArrayElement ? "[i]" : "";
            sb.Append($"{leftPreString}{argumentName}{leftPostString} = ");
            var rightString = isArrayElement ? $"{argumentName}List[i]" : $"dic[\"{jsonKey}\"]";

            //check option Enum
            var typeIsEnum = _enumList.ToArray().Contains(argumentType);
            if (typeIsEnum)
            {
                //custom enum
                sb.Append($"({argumentType})(int){rightString}.Number;");
                return sb.ToString();
            }

            //switch with arg type
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
                    sb.Append($"({argumentType}){rightString}.Number;");
                    break;
                case "TrackingDataType":
                case "HumanBodyBones":
                case "TextureWrapMode":
                case "GraphicsFormat":
                case "TextureFormat":
                    //normal enum
                    sb.Append($"({argumentType})(int){rightString}.Number;");
                    break;
                default:
                    //Dark class
                    if (!isArrayElement)
                    {
                        sb.Append($"{argumentType}.New(dic[\"{jsonKey}\"].DataDictionary);");
                    }
                    else
                    {
                        sb.Append($"{argumentType}.New({jsonKey}List[i].DataDictionary);");
                    }
                    break;
            }
            return sb.ToString();
        }

        private string GenerateJsonColorField(string argumentType, string argumentName, bool isArrayElement, string jsonKey)
        {
            var elementStrings = new string[] { "r", "g", "b", "a" };
            var elementNameStrings = new string[] { "R", "G", "B", "A" };
            var elementNum = elementStrings.Length;

            var cast = string.Empty;
            if (argumentType == "Color")
            {
                cast = "(float)";
            }
            else if (argumentType == "Color32")
            {
                cast = "(byte)";
            }

            var sb = new StringBuilder();
            if (!isArrayElement)
            {
                sb.AppendLine();    //For readability
            }
            var tab = isArrayElement ? $"\t\t" : $"\t";
            var arrayElement = isArrayElement ? $"[i]" : $"";
            var argNameString = isArrayElement ? $"{argumentName}List[i]" : $"dic[\"{jsonKey}\"]";
            var varRightString = _vectorOrColorAsDataList ? $"{argNameString}.DataList;" : $"{argNameString}.DataDictionary;";
            var dataName = $"{argumentName}Data";
            sb.AppendLine($"{tab}var {dataName} = {varRightString}");

            for (int i = 0; i < elementNum; i++)
            {
                var elementName = argumentName + elementNameStrings[i];
                var elementTarget = _vectorOrColorAsDataList ? $"[{i}]" : $"[\"{elementStrings[i]}\"]";
                sb.AppendLine($"{tab}var {elementName} = {cast}{dataName}{elementTarget}.Number;");
            }

            //constructor
            var varString = isArrayElement ? $"" : $"var ";
            sb.Append($"{tab}{varString}{argumentName}{arrayElement} = new {argumentType}(");
            for (int i = 0; i < elementNum; i++)
            {
                var elementName = argumentName + elementNameStrings[i];
                sb.Append($"{elementName}");
                if (i < elementNum - 1) { sb.Append(", "); }    //except last
            }
            sb.Append($");");

            return sb.ToString();
        }

        private string GenerateJsonVectorField(string argumentType, string argumentName, bool isArrayElement, string jsonKey)
        {
            var elementStrings = new string[] { "x", "y", "z", "w" };
            var elementNameStrings = new string[] { "X", "Y", "Z", "W" };

            var elementNum = 0;
            if (argumentType == "Vector2")
            {
                elementNum = 2;
            }
            else if (argumentType == "Vector3")
            {
                elementNum = 3;
            }
            else if (argumentType == "Vector4")
            {
                elementNum = 4;
            }
            else if (argumentType == "Quaternion")
            {
                elementNum = 4;
            }

            var sb = new StringBuilder();
            if (!isArrayElement)
            {
                sb.AppendLine();    //For readability
            }
            var tab = isArrayElement ? $"\t\t" : $"\t";
            var arrayElement = isArrayElement ? $"[i]" : $"";
            var argNameString = isArrayElement ? $"{argumentName}List[i]" : $"dic[\"{jsonKey}\"]";
            var varRightString = _vectorOrColorAsDataList ? $"{argNameString}.DataList;" : $"{argNameString}.DataDictionary;";
            var dataName = $"{argumentName}Data";
            sb.AppendLine($"{tab}var {dataName} = {varRightString}");

            for (int i = 0; i < elementNum; i++)
            {
                var elementName = argumentName + elementNameStrings[i];
                var elementTarget = _vectorOrColorAsDataList ? $"[{i}]" : $"[\"{elementStrings[i]}\"]";
                sb.AppendLine($"{tab}var {elementName} = (float){dataName}{elementTarget}.Number;");
            }

            //constructor
            var varString = isArrayElement ? $"" : $"var ";
            sb.Append($"{tab}{varString}{argumentName}{arrayElement} = new {argumentType}(");
            for (int i = 0; i < elementNum; i++)
            {
                var elementName = argumentName + elementNameStrings[i];
                sb.Append($"{elementName}");
                if (i < elementNum - 1) { sb.Append(", "); }    //except last
            }
            sb.Append($");");

            return sb.ToString();
        }

        /*------------------------------Utility----------------------------------*/
        private string GenerateDataTokenAssign(string pClassName, string enumName, List<string> fieldList)
        {
            var sb = new StringBuilder();

            //data token 
            sb.AppendLine($"\t// Make DataTokens");
            sb.AppendLine($"\tvar data = new DataToken[(int){enumName}.Count];");
            sb.AppendLine();

            var dataBase = $"\tdata[(int)";
            foreach (var field in fieldList)
            {
                var (argumentType, argumentName, pArgumentName, _) = GetTypeAndNamesFromField(field);
                if (string.IsNullOrEmpty(argumentType)) { continue; }

                var typeIsReference = CheckDataTokenTypeIsReference(argumentType);
                var arg = typeIsReference ? $"new DataToken({argumentName})" : $"{argumentName}";

                sb.AppendLine($"{dataBase}{enumName}.{pArgumentName}] = {arg};");
            }

            sb.AppendLine();
            sb.AppendLine($"\treturn ({pClassName})new DataList(data);");
            sb.AppendLine($"}}");
            return sb.ToString();
        }

        private void LoadScript(string text)
        {
            Debug.Log("LoadScript: " + text);
            var oneLineText = text.Replace("\r\n", "\n").Replace("\n", " "); //remove line break and join with space

            //namespace
            _nameSpace = ExtractNameSpace(oneLineText);

            //className
            _className = ExtractClassName(oneLineText);

            //Fields
            if (!string.IsNullOrEmpty(_className))
            {
                var fieldStrings = ExtractFields(oneLineText, _className);
                if (fieldStrings.Count > 0) { UpdateReorderableFieldList(fieldStrings); }
            }
        }

        private string ExtractNameSpace(string oneLineText)
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

        private string ExtractClassName(string oneLineText)
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

        private List<string> ExtractFields(string oneLineText, string className)
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
                    foreach (var word in words)
                    {
                        Debug.Log(word);
                    }
                    fields.AddRange(words);
                }
            }

            return fields;
        }

        private static string ToPascal(string text)
        {
            return Regex.Replace(text.Replace("_", " "), @"\b[a-z]", match => match.Value.ToUpper()).Replace(" ", "");
        }
    }
}
#endif