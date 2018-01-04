using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace VirtualFolder
{
    public class VirtualFolderWindow : EditorWindow
    {
        [MenuItem("Window/Virtual Folder")]
        private static void InitShow()
        {
            GetWindow<VirtualFolderWindow>();
        }

        public delegate void WindowItemCallback(VirtualFolderInfo info, Rect selectionRect);
        public static WindowItemCallback windowItemOnGUI;

        private static string s_ConfigPath = "Assets/Editor/VirtualFolder/VirtualFolderConfig.json";
        private static Styles s_Styles;

        [SerializeField]
        private TreeViewState m_TreeViewState;
        [SerializeField]
        private int m_CurrentListIndex;

        private bool m_CreateVirtualFolder;
        private string m_CreateName;
        private SearchField m_SearchField;
        private VirtualFolderTreeView m_TreeView;

        private VirtualFolderList m_VirtualFolderList;

        void Awake()
        {
            titleContent = EditorGUIUtility.IconContent("Project");
            titleContent.text = "Virtual Folder";
        }

        void OnEnable()
        {
        }

        void OnDisable()
        {
        }

        void OnGUI()
        {
            if (s_Styles == null)
                s_Styles = new Styles();
            if (!Initialized())
            {
                Init();
            }

            DrawToolBar();
            DrawCreate();
            DoTreeView();
            ExecuteCommands();
            HandleKeys();
        }

        private void DrawToolBar()
        {
            EditorGUI.BeginDisabledGroup(m_CreateVirtualFolder);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            Rect rect = GUILayoutUtility.GetRect(s_Styles.fileDropdownContent, EditorStyles.toolbarDropDown);
            if (EditorGUI.DropdownButton(rect, s_Styles.fileDropdownContent, FocusType.Passive, EditorStyles.toolbarDropDown))
            {
                GUIUtility.hotControl = 0;
                var createMenu = new GenericMenu();
                createMenu.AddItem(s_Styles.newFolderContent, false, CreateFolder);
                createMenu.AddItem(s_Styles.newFolderChildContent, false, CreateFolderChild);
                createMenu.AddSeparator(String.Empty);
                createMenu.AddItem(s_Styles.newVirtualFolderContent, false, CreateVirtualFolder);
                createMenu.AddSeparator(String.Empty);
                createMenu.AddItem(s_Styles.saveDropdownContent, false, SaveData);
                createMenu.AddItem(s_Styles.reloadDropdownContent, false, () => { CreateData(); SetTreeViewModelIndex(m_CurrentListIndex); });
                createMenu.DropDown(new Rect(rect.x, 17f, 0, 0));
            }
            rect = GUILayoutUtility.GetRect(s_Styles.viewDropdownContent, EditorStyles.toolbarDropDown);
            if (EditorGUI.DropdownButton(rect, s_Styles.viewDropdownContent, FocusType.Passive, EditorStyles.toolbarDropDown))
            {
                GUIUtility.hotControl = 0;
                var createMenu = new GenericMenu();
                var list = m_VirtualFolderList.rootList;
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    createMenu.AddItem(new GUIContent(list[i].name), i == m_CurrentListIndex, ViewVirtualFolder, i);
                }
                createMenu.DropDown(new Rect(rect.x, 17f, 0, 0));
            }
            GUILayout.FlexibleSpace();
            m_TreeView.searchString = m_SearchField.OnToolbarGUI(m_TreeView.searchString);
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawCreate()
        {
            if (!m_CreateVirtualFolder)
            {
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("OK", GUILayout.Width(60)))
            {
                m_CreateVirtualFolder = !CreateVirtualFolderDo();
            }
            if (GUILayout.Button("Cancel", GUILayout.Width(60)))
            {
                m_CreateVirtualFolder = false;
            }
            m_CreateName = EditorGUILayout.TextField(m_CreateName);
            EditorGUILayout.EndHorizontal();
        }

        void DoTreeView()
        {
            if (m_CreateVirtualFolder)
            {
                return;
            }
            if (m_VirtualFolderList.rootList != null && m_VirtualFolderList.rootList.Count > 0)
            {
                Rect rect = GUILayoutUtility.GetRect(0, 100000, 0, 100000);
                m_TreeView.OnGUI(rect);
            }
        }

        private void SearchFieldOnDownOrUpArrowKeyPressed()
        {
            m_TreeView.SetFocus();
        }

        public bool Initialized()
        {
            return m_TreeView != null;
        }

        private void Init()
        {
            if (Initialized())
            {
                return;
            }

            CreateData();
            m_SearchField = new SearchField();
            m_SearchField.downOrUpArrowKeyPressed += SearchFieldOnDownOrUpArrowKeyPressed;

            if (m_TreeViewState == null)
            {
                m_TreeViewState = new TreeViewState();
            }
            m_TreeView = new VirtualFolderTreeView(m_TreeViewState);
            m_TreeView.onGUIRowCallback += OnGUIItemCallback;
            SetTreeViewModelIndex(m_CurrentListIndex);
        }

        private void OnGUIItemCallback(VirtualFolderInfo info, Rect rect)
        {
            if (windowItemOnGUI == null)
            {
                return;
            }
            windowItemOnGUI(info, rect);
        }

        private void OnLostFocus()
        {
            if (m_TreeView != null)
            {
                m_TreeView.EndRename();
            }
        }

        private void ExecuteCommands()
        {
            Event current = Event.current;
            if (current.type != EventType.ExecuteCommand && current.type != EventType.ValidateCommand)
            {
                return;
            }

            bool flag = current.type == EventType.ExecuteCommand;
            if (current.commandName == "Delete" || current.commandName == "SoftDelete")
            {
                if (flag)
                {
                    DeleteFolder();
                }
                current.Use();
                GUIUtility.ExitGUI();
            }
            else if (current.commandName == "Duplicate")
            {
                if (flag)
                {
                    DuplicateFolder();
                }
                current.Use();
                GUIUtility.ExitGUI();
            }
            else if (current.commandName == "FrameSelected")
            {
                if (current.type == EventType.ExecuteCommand)
                {
                    FrameFolder();
                }
                current.Use();
                GUIUtility.ExitGUI();
            }
        }

        private void HandleKeys()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown || m_TreeView == null)
            {
                return;
            }
            if (m_TreeView.treeViewControlID != GUIUtility.keyboardControl)
            {
                return;
            }

            if (current.keyCode == KeyCode.D)
            {
                if (EditorGUI.actionKey && current.shift)
                {
                    CreateFolder();
                    current.Use();
                }
                else if (current.alt && current.shift)
                {
                    CreateFolderChild();
                    current.Use();
                }
            }
        }

        private void CreateData()
        {
            if (!File.Exists(s_ConfigPath))
            {
                m_VirtualFolderList = new VirtualFolderList();
                return;
            }
            string json = File.ReadAllText(s_ConfigPath);
            m_VirtualFolderList = VirtualFolderList.CreateFromString(json);
        }

        private void SaveData()
        {
            if (m_VirtualFolderList != null)
            {
                File.WriteAllText(s_ConfigPath, m_VirtualFolderList.SaveToString());
                AssetDatabase.ImportAsset(s_ConfigPath, ImportAssetOptions.Default);
            }
        }

        private void SetTreeViewModelIndex(int index)
        {
            if (m_VirtualFolderList.rootList == null)
            {
                return;
            }

            m_CurrentListIndex = -1;
            if (index >= 0 && index < m_VirtualFolderList.rootList.Count)
            {
                m_CurrentListIndex = index;
                m_TreeView.SetModel(m_VirtualFolderList.rootList[index]);
            }
        }

        private void CreateVirtualFolder()
        {
            m_CreateVirtualFolder = true;
        }

        private bool CreateVirtualFolderDo()
        {
            if (string.IsNullOrEmpty(m_CreateName))
            {
                return false;
            }

            m_TreeView.searchString = String.Empty;
            if (!m_VirtualFolderList.ContainsRoot(m_CreateName))
            {
                m_VirtualFolderList.AddRoot(m_CreateName);
            }
            SetTreeViewModelIndex(m_VirtualFolderList.rootCount - 1);
            m_CreateName = String.Empty;
            return true;
        }

        private void CreateFolder()
        {
            var selection = m_TreeViewState.selectedIDs;
            if (selection.Count == 0 || m_TreeView.hasSearch)
            {
                return;
            }

            VirtualFolderInfo info = m_VirtualFolderList.rootList[m_CurrentListIndex].Find(selection[0]);
            if (info != null)
            {
                if (info.parent != null)
                {
                    info = info.parent;
                }
                int id = info.AddChild();
                m_TreeView.Reload();
                m_TreeView.SetExpanded(info.id, true);
                m_TreeView.SetSelection(new List<int>() { id }, TreeViewSelectionOptions.RevealAndFrame);
                m_TreeView.BeginRename(m_TreeView.GetItemById(id));
            }
        }

        private void CreateFolderChild()
        {
            var selection = m_TreeViewState.selectedIDs;
            if (selection.Count == 0 || m_TreeView.hasSearch)
            {
                return;
            }

            VirtualFolderInfo info = m_VirtualFolderList.rootList[m_CurrentListIndex].Find(selection[0]);
            if (info != null)
            {
                int id = info.AddChild();
                m_TreeView.Reload();
                m_TreeView.SetExpanded(info.id, true);
                m_TreeView.SetSelection(new List<int>() { id }, TreeViewSelectionOptions.RevealAndFrame);
                m_TreeView.BeginRename(m_TreeView.GetItemById(id));
            }
        }

        private void DeleteFolder()
        {
            var selection = m_TreeViewState.selectedIDs;
            if (selection.Count == 0 || m_TreeView.hasSearch)
            {
                return;
            }

            VirtualFolderInfo info = m_VirtualFolderList.rootList[m_CurrentListIndex].Find(selection[0]);
            if (info != null)
            {
                if (info.parent != null)
                {
                    info.parent.RemoveChild(info);
                    m_TreeView.Reload();
                }
            }
        }

        private void DuplicateFolder()
        {
            CreateFolder();
        }

        private void FrameFolder()
        {
            var selection = m_TreeViewState.selectedIDs;
            if (selection.Count == 0)
            {
                return;
            }
            m_TreeView.FrameItemById(selection[0], true);
        }

        private void ViewVirtualFolder(object obj)
        {
            SetTreeViewModelIndex((int)obj);
        }

        private class Styles
        {
            public readonly GUIContent fileDropdownContent = new GUIContent("File");
            public readonly GUIContent viewDropdownContent = new GUIContent("View");
            public readonly GUIContent newFolderContent = new GUIContent("New Folder %#d");
            public readonly GUIContent newFolderChildContent = new GUIContent("New Folder Child &#d");
            public readonly GUIContent newVirtualFolderContent = new GUIContent("New Virtual Folder");
            public readonly GUIContent saveDropdownContent = new GUIContent("Save Config");
            public readonly GUIContent reloadDropdownContent = new GUIContent("Reload Config");

            private static GUIStyle GetStyle(string styleName)
            {
                return (GUIStyle)styleName;
            }
        }
    }
}