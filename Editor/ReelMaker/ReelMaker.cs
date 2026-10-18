using System;
using System.Collections.Generic;
using System.IO;
using AX.UdonSlot.REV1_0;
using AX.UdonSlot.REV1_0.Serialize;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UdonSharpEditor;

namespace AX.UdonSlot.REV1_0.Editor.ReelMaker
{
    public class ReelMaker : EditorWindow
    {
        private const string UIPath = "Packages/com.axtuki1.udonslot/Editor/ReelMaker/ReelMakerWindow.uxml";
        private static readonly List<int> TextureSizes = new List<int> { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 };
        private static readonly string[] ReelNames = { "左リール", "中リール", "右リール" };

        private static readonly Color HeaderColor = new Color(0.17f, 0.17f, 0.17f);
        private static readonly Color RowColor = new Color(0.18f, 0.18f, 0.18f);
        private static readonly Color SelectedColor = new Color(0.18f, 0.35f, 0.54f);
        private static readonly Color AccentColor = new Color(0.30f, 0.56f, 0.84f);

        [Serializable]
        private class SymbolOptions
        {
            public string symbol;
            public ReelPicture pictureOverride;
            public bool customSize;
            public int width = 256;
            public int height = 128;
        }

        [SerializeField] private ReelData[] reels = new ReelData[3];
        [SerializeField] private int outputWidth = 1024, outputHeight = 4096;
        [SerializeField] private int symbolWidth = 320, symbolHeight = 205;
        [SerializeField] private int bottomPadding = 0, sidePadding = 8;
        [SerializeField] private List<SymbolOptions> symbolOptions = new List<SymbolOptions>();
        [SerializeField] private int addTarget;
        [SerializeField] private int selectedColumn = -1, selectedRow = -1;
        [SerializeField] private ReelData selectedData;
        [SerializeField] private string catalogSearch = "";
        private readonly List<ReelPicture> catalogPictures = new List<ReelPicture>();
        private readonly Dictionary<string, List<ReelPicture>> pictures = new Dictionary<string, List<ReelPicture>>(StringComparer.Ordinal);
        private readonly ScrollView[] reelViews = new ScrollView[3];
        private readonly ObjectField[] reelFields = new ObjectField[3];
        private Texture2D preview;
        private bool previewDirty = true;
        private bool ready;

        [MenuItem("UdonSlot/ReelMaker")]
        private static void ShowWindow()
        {
            var window = GetWindow<ReelMaker>();
            window.titleContent = new GUIContent("ReelMaker");
            window.minSize = new Vector2(820, 550);
        }

        private void OnEnable()
        {
            EditorApplication.projectChanged += Reload;
            EditorApplication.hierarchyChanged += RefreshData;
            Undo.undoRedoPerformed += RefreshData;
        }

        private void OnDisable()
        {
            ready = false;
            EditorApplication.projectChanged -= Reload;
            EditorApplication.hierarchyChanged -= RefreshData;
            Undo.undoRedoPerformed -= RefreshData;
            if (preview != null) DestroyImmediate(preview);
        }

        private void OnFocus()
        {
            RefreshData();
        }

        public void CreateGUI()
        {
            ready = false;
            minSize = new Vector2(820, 550);
            if (reels == null || reels.Length != 3) Array.Resize(ref reels, 3);
            rootVisualElement.Clear();
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UIPath);
            if (template == null)
            {
                rootVisualElement.Add(new HelpBox("ReelMakerWindow.uxmlが見つかりません。", HelpBoxMessageType.Error));
                return;
            }
            template.CloneTree(rootVisualElement);
            var controller = rootVisualElement.Q<ObjectField>("Controller");
            controller.objectType = typeof(ReelOutputController);
            controller.allowSceneObjects = true;
            controller.RegisterValueChangedCallback(evt =>
            {
                var source = evt.newValue as ReelOutputController;
                if (source == null) return;
                for (int i = 0; i < 3; i++)
                {
                    reels[i] = source.reelDatas != null && i < source.reelDatas.Length ? source.reelDatas[i] : null;
                    reelFields[i].SetValueWithoutNotify(reels[i]);
                }
                RefreshData();
            });
            var columns = rootVisualElement.Q("Reels");
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                var column = new VisualElement();
                column.style.flexGrow = 1;
                column.style.flexBasis = 0;
                column.style.marginRight = 4;
                var title = new Label(ReelNames[i]);
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.backgroundColor = HeaderColor;
                title.style.height = 24;
                title.style.paddingLeft = 8;
                title.style.unityTextAlign = TextAnchor.MiddleLeft;
                title.style.marginBottom = 2;
                column.Add(title);
                var field = new ObjectField { objectType = typeof(ReelData), allowSceneObjects = true, value = reels[i] };
                field.RegisterValueChangedCallback(evt => { reels[index] = evt.newValue as ReelData; RefreshData(); });
                reelFields[i] = field;
                column.Add(field);
                column.Add(new Button(() => CreateOrResetReel(index)) { name = "ReelAction_" + index, text = "新規ReelDataを作成" });
                reelViews[i] = new ScrollView();
                reelViews[i].style.flexGrow = 1;
                reelViews[i].style.minHeight = 0;
                reelViews[i].contentViewport.RegisterCallback<GeometryChangedEvent>(_ => FitReelRows());
                column.Add(reelViews[i]);
                columns.Add(column);
            }
            var catalogToolbar = rootVisualElement.Q("CatalogToolbar");
            var targetField = new PopupField<string>("追加先", new List<string> { "左リール", "中リール", "右リール" }, Mathf.Clamp(addTarget, 0, 2));
            targetField.name = "AddTarget";
            targetField.style.width = 180;
            targetField.labelElement.style.minWidth = 45;
            targetField.RegisterValueChangedCallback(_ => addTarget = targetField.index);
            catalogToolbar.Add(targetField);
            var search = new ToolbarSearchField { value = catalogSearch };
            search.style.flexGrow = 1;
            search.style.flexBasis = 110;
            search.tooltip = "図柄名・アセット名・パスで検索";
            search.RegisterValueChangedCallback(evt => { catalogSearch = evt.newValue; RefreshCatalog(); });
            catalogToolbar.Add(search);
            catalogToolbar.Add(new Button(() => { ClearSelection(); UpdateSelection(); }) { text = "解除" });
            var selectionActions = rootVisualElement.Q("SelectionActions");
            selectionActions.Add(new Button(() => EditSymbol(selectedColumn, selectedRow, -1)) { name = "MoveUp", text = "↑" });
            selectionActions.Add(new Button(() => EditSymbol(selectedColumn, selectedRow, 1)) { name = "MoveDown", text = "↓" });
            selectionActions.Add(new Button(() => EditSymbol(selectedColumn, selectedRow, 2)) { text = "複製" });
            selectionActions.Add(new Button(() => EditSymbol(selectedColumn, selectedRow, 0)) { text = "削除" });
            var settings = rootVisualElement.Q("Settings");
            AddTextureSize(settings, "画像の幅", outputWidth, value => outputWidth = value);
            AddTextureSize(settings, "画像の高さ", outputHeight, value => outputHeight = value);
            AddInteger(settings, "図柄の幅", symbolWidth, value => symbolWidth = value);
            AddInteger(settings, "図柄の高さ", symbolHeight, value => symbolHeight = value);
            AddInteger(settings, "下余白", bottomPadding, value => bottomPadding = value);
            AddInteger(settings, "左右余白", sidePadding, value => sidePadding = value);
            rootVisualElement.Q<Button>("Reload").clicked += Reload;
            rootVisualElement.Q<Button>("PreviewButton").clicked += BuildPreview;
            rootVisualElement.Q<Button>("Export").clicked += Export;
            var previewContainer = rootVisualElement.Q<IMGUIContainer>("Preview");
            previewContainer.onGUIHandler = DrawPreview;
            previewContainer.style.flexShrink = 0;
            previewContainer.RegisterCallback<GeometryChangedEvent>(_ => UpdatePreviewSize());
            ready = true;
            Reload();
        }

        private void AddTextureSize(VisualElement parent, string label, int value, Action<int> setter)
        {
            var field = new PopupField<int>(label, TextureSizes, Math.Max(0, TextureSizes.IndexOf(value)));
            field.RegisterValueChangedCallback(evt => { setter(evt.newValue); SettingsChanged(); });
            parent.Add(field);
        }

        private void AddInteger(VisualElement parent, string label, int value, Action<int> setter)
        {
            var field = new IntegerField(label) { value = value, isDelayed = true };
            field.RegisterValueChangedCallback(evt => { setter(evt.newValue); SettingsChanged(); });
            parent.Add(field);
        }

        private void Reload()
        {
            if (!ready) return;
            pictures.Clear();
            catalogPictures.Clear();
            var guids = AssetDatabase.FindAssets("t:ReelPicture");
            Array.Sort(guids, StringComparer.Ordinal);
            foreach (string guid in guids)
            {
                var picture = AssetDatabase.LoadAssetAtPath<ReelPicture>(AssetDatabase.GUIDToAssetPath(guid));
                if (picture == null) continue;
                catalogPictures.Add(picture);
                if (string.IsNullOrEmpty(picture.pictureName)) continue;
                if (!pictures.TryGetValue(picture.pictureName, out var candidates))
                    pictures[picture.pictureName] = candidates = new List<ReelPicture>();
                candidates.Add(picture);
            }
            catalogPictures.Sort((a, b) =>
            {
                int comparison = string.Compare(a.pictureName, b.pictureName, StringComparison.Ordinal);
                return comparison != 0 ? comparison : string.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b), StringComparison.Ordinal);
            });
            RefreshCatalog();
            RefreshData();
        }

        private void RefreshCatalog()
        {
            if (!ready) return;
            var catalog = rootVisualElement.Q("Catalog");
            catalog.Clear();
            foreach (var picture in catalogPictures)
            {
                if (picture == null) continue;
                string path = AssetDatabase.GetAssetPath(picture);
                string searchable = picture.pictureName + " " + picture.name + " " + path;
                if (!string.IsNullOrEmpty(catalogSearch) && searchable.IndexOf(catalogSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var tile = new VisualElement { tooltip = path };
                tile.style.width = Length.Percent(100);
                tile.style.height = 28;
                tile.style.flexShrink = 0;
                tile.style.flexDirection = FlexDirection.Row;
                var replace = new Button(() => ReplaceSelectedPicture(picture)) { name = "ReplaceSymbol", tooltip = picture.pictureName + "\n" + path + "\nクリックで選択中のコマを置換" };
                replace.style.flexDirection = FlexDirection.Row;
                replace.style.alignItems = Align.Center;
                replace.style.flexGrow = 1;
                replace.style.flexBasis = 0;
                replace.style.minWidth = 0;
                replace.style.marginTop = replace.style.marginBottom = 0;
                replace.style.paddingTop = replace.style.paddingBottom = 0;
                float aspect = GetSourceAspect(picture.picture);
                float imageHeight = Mathf.Min(22, 52 / aspect);
                var thumbnail = new Image { name = "SymbolPreview", image = picture.picture, scaleMode = ScaleMode.StretchToFill };
                thumbnail.style.width = imageHeight * aspect;
                thumbnail.style.height = imageHeight;
                thumbnail.style.flexShrink = 0;
                var imageArea = new VisualElement();
                imageArea.style.width = 56;
                imageArea.style.flexShrink = 0;
                imageArea.style.alignItems = Align.Center;
                imageArea.Add(thumbnail);
                replace.Add(imageArea);
                var label = new Label(string.IsNullOrEmpty(picture.pictureName) ? "(図柄名未設定)" : picture.pictureName);
                label.style.fontSize = 11;
                label.style.flexGrow = 1;
                label.style.minWidth = 0;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                replace.Add(label);
                replace.SetEnabled(HasSelection() && !string.IsNullOrEmpty(picture.pictureName));
                tile.Add(replace);
                var add = new Button(() => AppendPicture(picture)) { text = "＋", tooltip = "追加先リールの末尾に追加" };
                add.style.width = 26;
                add.style.flexShrink = 0;
                add.style.marginTop = add.style.marginBottom = 0;
                add.SetEnabled(!string.IsNullOrEmpty(picture.pictureName) && !EditorApplication.isPlaying);
                tile.Add(add);
                catalog.Add(tile);
            }
            if (catalog.childCount == 0) catalog.Add(new Label("該当するReelPictureがありません。"));
        }

        private void CreateOrResetReel(int index)
        {
            if (EditorApplication.isPlaying) return;
            if (reels[index] == null)
            {
                CreateReel(index);
                return;
            }
            if (!CanEditReel(index)) return;
            var data = reels[index];
            int count = data.symbols != null ? data.symbols.Length : 0;
            if (!EditorUtility.DisplayDialog("ReelDataをリセット",
                    $"{ReelNames[index]} の「{data.name}」をリセットしますか？\n\n{count}個の図柄をすべて削除します。ReelData自体と参照は保持されます。\n同じReelDataを参照している箇所にも反映されます。操作はUndoで戻せます。",
                    "リセット", "キャンセル")) return;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Reset ReelData");
            WriteSymbols(index, new List<string>());
        }

        private void CreateReel(int index)
        {
            if (EditorApplication.isPlaying) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create ReelData");
            var go = new GameObject(new[] { "ReelData_Left", "ReelData_Center", "ReelData_Right" }[index]);
            UnityEditor.SceneManagement.StageUtility.PlaceGameObjectInCurrentStage(go);
            Undo.RegisterCreatedObjectUndo(go, "Create ReelData");
            Undo.RecordObject(this, "Assign ReelData");
            reels[index] = UdonSharpUndo.AddComponent<ReelData>(go);
            WriteSymbols(index, new List<string>());
            Undo.CollapseUndoOperations(group);
            EditorGUIUtility.PingObject(go);
        }

        private void AppendPicture(ReelPicture picture)
        {
            if (EditorApplication.isPlaying || picture == null || string.IsNullOrEmpty(picture.pictureName)) return;
            int index = Mathf.Clamp(addTarget, 0, 2);
            if (reels[index] == null) CreateReel(index);
            if (!CanEditReel(index)) return;
            Undo.RecordObject(this, "Choose ReelPicture");
            GetOptions(picture.pictureName).pictureOverride = picture;
            var symbols = new List<string>(reels[index].symbols ?? Array.Empty<string>());
            symbols.Add(picture.pictureName);
            WriteSymbols(index, symbols);
        }

        private bool CanEditReel(int index)
        {
            return index >= 0 && index < reels.Length && !EditorApplication.isPlaying && reels[index] != null && !EditorUtility.IsPersistent(reels[index]);
        }

        private bool HasSelection()
        {
            return selectedColumn >= 0 && selectedColumn < 3 && CanEditReel(selectedColumn)
                && selectedData == reels[selectedColumn] && selectedData.symbols != null
                && selectedRow >= 0 && selectedRow < selectedData.symbols.Length;
        }

        private void ClearSelection()
        {
            selectedColumn = selectedRow = -1;
            selectedData = null;
        }

        private void SelectSymbol(int column, int row)
        {
            if (!CanEditReel(column) || reels[column].symbols == null || row < 0 || row >= reels[column].symbols.Length) return;
            selectedColumn = column;
            selectedRow = row;
            selectedData = reels[column];
            addTarget = column;
            rootVisualElement.Q<PopupField<string>>("AddTarget").SetValueWithoutNotify(new[] { "左リール", "中リール", "右リール" }[column]);
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            if (!ready) return;
            if (!HasSelection()) ClearSelection();
            rootVisualElement.Query<VisualElement>(className: "reel-symbol").ForEach(card =>
            {
                bool selected = card.name == $"ReelSymbol_{selectedColumn}_{selectedRow}";
                var color = selected ? AccentColor : Color.clear;
                card.style.borderLeftColor = card.style.borderRightColor = card.style.borderTopColor = card.style.borderBottomColor = color;
                card.style.backgroundColor = selected ? SelectedColor : RowColor;
            });
            rootVisualElement.Q<Label>("CatalogSelection").text = HasSelection()
                ? $"{ReelNames[selectedColumn]} {selectedData.symbols.Length - selectedRow}  {selectedData.symbols[selectedRow]}"
                : "";
            var actions = rootVisualElement.Q("SelectionActions");
            actions.SetEnabled(HasSelection());
            actions.Q<Button>("MoveUp").SetEnabled(HasSelection() && selectedRow > 0);
            actions.Q<Button>("MoveDown").SetEnabled(HasSelection() && selectedRow + 1 < selectedData.symbols.Length);
            RefreshCatalog();
        }

        private void ReplaceSelectedPicture(ReelPicture picture)
        {
            if (!HasSelection() || picture == null || string.IsNullOrEmpty(picture.pictureName)) return;
            Undo.RecordObject(this, "Replace ReelPicture");
            GetOptions(picture.pictureName).pictureOverride = picture;
            var symbols = new List<string>(selectedData.symbols);
            symbols[selectedRow] = picture.pictureName;
            WriteSymbols(selectedColumn, symbols);
        }

        private void WriteSymbols(int index, List<string> symbols)
        {
            if (!CanEditReel(index)) return;
            var data = new SerializedObject(reels[index]);
            var property = data.FindProperty("symbols");
            property.arraySize = symbols.Count;
            for (int i = 0; i < symbols.Count; i++) property.GetArrayElementAtIndex(i).stringValue = symbols[i];
            data.ApplyModifiedProperties();
            RefreshData();
        }

        private void EditSymbol(int column, int row, int operation)
        {
            if (!CanEditReel(column) || reels[column].symbols == null) return;
            var symbols = new List<string>(reels[column].symbols);
            if (row < 0 || row >= symbols.Count) return;
            Undo.RecordObject(this, "Edit Reel Selection");
            if (HasSelection() && selectedColumn == column)
            {
                if (operation == 0)
                {
                    if (selectedRow == row) ClearSelection();
                    else if (selectedRow > row) selectedRow--;
                }
                else if (operation == 2 && selectedRow > row) selectedRow++;
                else if ((operation == -1 || operation == 1) && row + operation >= 0 && row + operation < symbols.Count)
                {
                    if (selectedRow == row) selectedRow += operation;
                    else if (selectedRow == row + operation) selectedRow = row;
                }
            }
            if (operation == 0) symbols.RemoveAt(row);
            else if (operation == 2) symbols.Insert(row + 1, symbols[row]);
            else
            {
                int destination = row + operation;
                if (destination < 0 || destination >= symbols.Count) return;
                string symbol = symbols[row];
                symbols[row] = symbols[destination];
                symbols[destination] = symbol;
            }
            WriteSymbols(column, symbols);
        }

        private SymbolOptions GetOptions(string symbol)
        {
            var options = symbolOptions.Find(item => item.symbol == symbol);
            if (options != null) return options;
            options = new SymbolOptions { symbol = symbol, width = symbolWidth, height = symbolHeight };
            symbolOptions.Add(options);
            return options;
        }

        private ReelPicture Resolve(string symbol, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(symbol)) { error = "図柄名が空です"; return null; }
            var options = GetOptions(symbol);
            if (options.pictureOverride != null)
            {
                if (!string.Equals(options.pictureOverride.pictureName, symbol, StringComparison.Ordinal))
                { error = "指定したReelPictureの図柄名が一致しません"; return null; }
                if (options.pictureOverride.picture == null) error = "画像が未設定です";
                return options.pictureOverride;
            }
            if (!pictures.TryGetValue(symbol, out var candidates) || candidates.Count == 0)
            { error = "ReelPictureが見つかりません"; return null; }
            if (candidates.Count > 1)
            { error = "同名のReelPictureが複数あります"; return null; }
            if (candidates[0].picture == null) error = "画像が未設定です";
            return candidates[0];
        }

        private int MaxCount()
        {
            int count = 0;
            foreach (var reel in reels) if (reel != null && reel.symbols != null) count = Math.Max(count, reel.symbols.Length);
            return count;
        }

        private void RefreshData()
        {
            if (!ready) return;
            var used = new HashSet<string>(StringComparer.Ordinal);
            var optionsPanel = rootVisualElement.Q("SymbolSettings");
            optionsPanel.Clear();
            for (int column = 0; column < 3; column++)
            {
                reelFields[column].SetValueWithoutNotify(reels[column]);
                var reelAction = rootVisualElement.Q<Button>("ReelAction_" + column);
                reelAction.text = reels[column] != null ? "リセット" : "新規ReelDataを作成";
                reelAction.SetEnabled(!EditorApplication.isPlaying && (reels[column] == null || CanEditReel(column)));
                var list = reelViews[column];
                list.Clear();
                var symbols = reels[column] != null ? reels[column].symbols : null;
                list.Add(new Label(symbols == null ? "" : symbols.Length + " コマ"));
                if (reels[column] != null && EditorUtility.IsPersistent(reels[column]))
                    list.Add(new HelpBox("編集するにはPrefabを開くか、シーン上のReelDataを指定してください。", HelpBoxMessageType.Info));
                if (symbols == null) continue;
                for (int row = 0; row < symbols.Length; row++)
                {
                    string symbol = symbols[row];
                    var picture = Resolve(symbol, out string error);
                    var card = new VisualElement { name = $"ReelSymbol_{column}_{row}" };
                    card.AddToClassList("reel-symbol");
                    card.style.borderLeftWidth = card.style.borderRightWidth = card.style.borderTopWidth = card.style.borderBottomWidth = 1;
                    card.style.flexShrink = 0;
                    card.style.backgroundColor = RowColor;
                    int reelIndex = column, symbolIndex = row;
                    var select = new Button(() => SelectSymbol(reelIndex, symbolIndex)) { name = "SelectSymbol" };
                    select.style.flexDirection = FlexDirection.Row;
                    select.style.alignItems = Align.Center;
                    select.style.flexGrow = 1;
                    select.style.minHeight = 0;
                    select.style.marginTop = select.style.marginBottom = select.style.marginLeft = select.style.marginRight = 0;
                    select.style.paddingTop = select.style.paddingBottom = 0;
                    select.style.backgroundColor = Color.clear;
                    var number = new Label((symbols.Length - row).ToString());
                    number.style.width = 22;
                    number.style.flexShrink = 0;
                    number.style.fontSize = 11;
                    select.Add(number);
                    var texture = picture != null ? picture.picture : null;
                    float aspect = GetSourceAspect(texture);
                    var thumbnail = new Image { name = "SymbolPreview", image = texture, scaleMode = ScaleMode.StretchToFill };
                    thumbnail.style.flexShrink = 0;
                    thumbnail.style.backgroundColor = Color.black;
                    select.Add(thumbnail);
                    select.RegisterCallback<GeometryChangedEvent>(evt =>
                    {
                        float height = Mathf.Max(0, Mathf.Min(evt.newRect.height - 4, evt.newRect.width * 0.5f / aspect));
                        thumbnail.style.height = height;
                        thumbnail.style.width = height * aspect;
                    });
                    var name = new Label(symbol) { tooltip = error == null ? symbol : symbol + ": " + error };
                    name.style.flexGrow = 1;
                    name.style.flexShrink = 1;
                    name.style.minWidth = 0;
                    name.style.fontSize = 10;
                    name.style.overflow = Overflow.Hidden;
                    name.style.textOverflow = TextOverflow.Ellipsis;
                    select.Add(name);
                    card.Add(select);
                    if (error != null)
                    {
                        name.style.color = new Color(1f, 0.65f, 0.3f);
                    }
                    list.Add(card);
                    if (!string.IsNullOrEmpty(symbol) && used.Add(symbol)) AddSymbolSettings(optionsPanel, symbol);
                }
            }
            UpdateSelection();
            FitReelRows();
            SettingsChanged();
        }

        private void FitReelRows()
        {
            if (!ready) return;
            int count = MaxCount();
            if (count <= 0) return;
            float available = float.MaxValue;
            foreach (var view in reelViews)
            {
                if (view == null || view.contentViewport.contentRect.height <= 0 || float.IsNaN(view.contentViewport.contentRect.height)) return;
                float header = 0;
                foreach (var child in view.contentContainer.Children())
                    if (!child.ClassListContains("reel-symbol")) header += float.IsNaN(child.layout.height) ? 20 : Mathf.Max(20, child.layout.height);
                available = Mathf.Min(available, view.contentViewport.contentRect.height - header - 4);
            }
            // 小さなウィンドウでは可読性を優先して22pxを下限とし、それ以下はスクロールにする。
            float height = Mathf.Clamp(Mathf.Floor(available / count), 22, 64);
            rootVisualElement.Query<VisualElement>(className: "reel-symbol").ForEach(card => card.style.height = height);
        }

        private static Image CreateSymbolPreview(Texture2D texture)
        {
            float sourceAspect = GetSourceAspect(texture);
            // NPOTのインポートで縦横が別々に拡縮されていても、元ファイルの比率に戻して表示する。
            var image = new Image { name = "SymbolPreview", image = texture, scaleMode = ScaleMode.StretchToFill };
            image.style.width = Length.Percent(100);
            image.style.minWidth = 0;
            image.style.flexShrink = 0;
            image.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                if (image.image == null || evt.newRect.width <= 0) return;
                float height = evt.newRect.width / sourceAspect;
                if (!Mathf.Approximately(image.resolvedStyle.height, height)) image.style.height = height;
            });
            return image;
        }

        private static float GetSourceAspect(Texture2D texture)
        {
            if (texture == null) return 1f;
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) as TextureImporter;
            if (importer != null)
            {
                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                if (width > 0 && height > 0) return (float)width / height;
            }
            return (float)texture.width / texture.height;
        }

        private void AddSymbolSettings(VisualElement parent, string symbol)
        {
            var options = GetOptions(symbol);
            var group = new Foldout { text = symbol, value = false };
            var pictureField = new ObjectField("画像") { objectType = typeof(ReelPicture), allowSceneObjects = false, value = options.pictureOverride };
            pictureField.tooltip = "未指定: 図柄名から自動";
            pictureField.RegisterValueChangedCallback(evt => { options.pictureOverride = evt.newValue as ReelPicture; RefreshData(); });
            group.Add(pictureField);
            var custom = new Toggle("個別サイズ") { value = options.customSize };
            var dimensions = new VisualElement();
            AddInteger(dimensions, "幅", options.width, value => options.width = value);
            AddInteger(dimensions, "高さ", options.height, value => options.height = value);
            dimensions.SetEnabled(options.customSize);
            custom.RegisterValueChangedCallback(evt => { options.customSize = evt.newValue; dimensions.SetEnabled(evt.newValue); SettingsChanged(); });
            group.Add(custom);
            group.Add(dimensions);
            parent.Add(group);
        }

        private string Validate()
        {
            foreach (var reel in reels)
                if (reel == null || reel.symbols == null || reel.symbols.Length == 0) return "左・中・右のReelDataを指定してください。各リールに1つ以上の図柄が必要です。";
            string layoutError = ReelTextureLayout.Validate(outputWidth, outputHeight, MaxCount(), bottomPadding, sidePadding);
            if (layoutError != null) return layoutError;
            if (outputWidth > SystemInfo.maxTextureSize || outputHeight > SystemInfo.maxTextureSize) return "この環境の最大テクスチャサイズを超えています。";
            if (symbolWidth <= 0 || symbolHeight <= 0) return "図柄の幅・高さは1 px以上にしてください。";
            foreach (var reel in reels)
                foreach (var symbol in reel.symbols)
                {
                    Resolve(symbol, out string error);
                    if (error != null) return $"{symbol ?? "(空)"}: {error}。図柄ごとの設定を確認してください。";
                    var options = GetOptions(symbol);
                    if (options.customSize && (options.width <= 0 || options.height <= 0)) return symbol + ": 個別サイズは1 px以上にしてください。";
                }
            return null;
        }

        private void SettingsChanged()
        {
            if (!ready) return;
            previewDirty = true;
            UpdatePreviewSize();
            int count = MaxCount();
            var summary = rootVisualElement.Q<Label>("LayoutSummary");
            summary.text = count > 0
                ? $"1コマ  {(double)outputWidth / 3 - sidePadding * 2:0.#} × {(double)outputHeight / count - bottomPadding:0.#} px"
                : "";
            string error = Validate();
            var messages = rootVisualElement.Q("Messages");
            messages.Clear();
            if (error != null) messages.Add(new HelpBox(error, HelpBoxMessageType.Warning));
            rootVisualElement.Q<Button>("PreviewButton").SetEnabled(error == null);
            rootVisualElement.Q<Button>("Export").SetEnabled(error == null);
            rootVisualElement.Q<Label>("PreviewStatus").text = "";
            Repaint();
        }

        private void BuildPreview()
        {
            // Inspectorからの変更も生成時に読み直す。
            Reload();
            string error = Validate();
            if (error != null) return;
            try
            {
                var generated = GenerateTexture();
                if (preview != null) DestroyImmediate(preview);
                preview = generated;
                previewDirty = false;
                UpdatePreviewSize();
                rootVisualElement.Q<Label>("PreviewStatus").text = $"{preview.width} × {preview.height} px";
                Repaint();
            }
            catch (Exception exception)
            {
                rootVisualElement.Q("Messages").Add(new HelpBox("画像を生成できませんでした: " + exception.Message, HelpBoxMessageType.Error));
                Debug.LogException(exception);
            }
        }

        private Texture2D GenerateTexture()
        {
            var result = new Texture2D(outputWidth, outputHeight, TextureFormat.RGBA32, false);
            try
            {
                result.SetPixels32(new Color32[outputWidth * outputHeight]);
                int count = MaxCount();
                for (int column = 0; column < 3; column++)
                {
                    var symbols = reels[column].symbols;
                    for (int row = 0; row < symbols.Length; row++)
                    {
                        var source = Resolve(symbols[row], out _).picture;
                        var options = GetOptions(symbols[row]);
                        int drawWidth = options.customSize ? options.width : symbolWidth;
                        int drawHeight = options.customSize ? options.height : symbolHeight;
                        var cell = ReelTextureLayout.GetCell(outputWidth, outputHeight, count, column, row, bottomPadding, sidePadding);
                        int width = Math.Min(drawWidth, cell.Width);
                        int height = Math.Min(drawHeight, cell.Height);
                        // 拡縮後の画像の中心だけをサンプリングする。Read/Write設定の変更は不要。
                        var scale = new Vector2((float)width / drawWidth, (float)height / drawHeight);
                        var offset = (Vector2.one - scale) * 0.5f;
                        var pixels = ReadCroppedPixels(source, width, height, scale, offset);
                        int x = cell.X + (cell.Width - width) / 2;
                        int top = cell.Y + (cell.Height - height) / 2;
                        result.SetPixels32(x, outputHeight - top - height, width, height, pixels);
                    }
                }
                result.Apply(false, false);
                return result;
            }
            catch { DestroyImmediate(result); throw; }
        }

        private static Color32[] ReadCroppedPixels(Texture2D source, int width, int height, Vector2 scale, Vector2 offset)
        {
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D readable = null;
            bool oldSrgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(source, temporary, scale, offset);
                RenderTexture.active = temporary;
                readable = new Texture2D(width, height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = oldSrgb;
                if (readable != null) DestroyImmediate(readable);
                RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private void UpdatePreviewSize()
        {
            var container = rootVisualElement.Q<IMGUIContainer>("Preview");
            if (container == null) return;
            float width = container.contentRect.width;
            float height = preview != null && !previewDirty && width > 0 && !float.IsNaN(width)
                ? width * preview.height / preview.width
                : 0;
            if (!Mathf.Approximately(container.resolvedStyle.height, height)) container.style.height = height;
        }

        private void DrawPreview()
        {
            if (preview == null || previewDirty) return;
            var container = rootVisualElement.Q<IMGUIContainer>("Preview");
            var rect = new Rect(0, 0, container.contentRect.width, container.contentRect.height);
            if (rect.width <= 0 || rect.height <= 0) return;
            EditorGUI.DrawTextureTransparent(rect, preview, ScaleMode.ScaleToFit, (float)preview.width / preview.height);
        }

        private void Export()
        {
            BuildPreview();
            if (preview == null || previewDirty) return;
            string path = EditorUtility.SaveFilePanelInProject("リール画像を書き出す", "ReelTexture", "png", "PNGの保存先を選択してください。");
            if (string.IsNullOrEmpty(path)) return;
            // 入力の図柄画像を上書きすると次回生成時の入力が変わるため拒否する。
            foreach (var reel in reels)
                foreach (var symbol in reel.symbols)
                    if (string.Equals(AssetDatabase.GetAssetPath(Resolve(symbol, out _).picture), path, StringComparison.OrdinalIgnoreCase))
                    {
                        EditorUtility.DisplayDialog("保存先を変更してください", "使用中の図柄画像には上書きできません。", "OK");
                        return;
                    }
            try
            {
                File.WriteAllBytes(path, preview.EncodeToPNG());
                AssetDatabase.ImportAsset(path);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.maxTextureSize = Math.Max(outputWidth, outputHeight);
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
                rootVisualElement.Q<Label>("PreviewStatus").text = "保存しました: " + path;
            }
            catch (Exception exception)
            {
                rootVisualElement.Q("Messages").Add(new HelpBox("保存に失敗しました: " + exception.Message, HelpBoxMessageType.Error));
                Debug.LogException(exception);
            }
        }
    }
}
