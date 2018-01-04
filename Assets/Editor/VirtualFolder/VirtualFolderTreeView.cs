using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VirtualFolder
{
    public class VirtualFolderTreeView : TreeView
    {
        private VirtualFolderInfo m_Infos;
        private static GUIStyle lineStyle = new GUIStyle("PR Label");

        public System.Action<VirtualFolderInfo, Rect, TreeView> onGUIRowCallback { get; set; }

        public VirtualFolderTreeView(TreeViewState state) : base(state)
        {
            lineStyle.alignment = TextAnchor.MiddleRight;
            lineStyle.padding.right = 3;
        }

        public void SetModel(VirtualFolderInfo infos)
        {
            m_Infos = infos;
            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            return new TreeViewItem { id = 0, depth = -1 };
        }

        protected override IList<TreeViewItem> BuildRows(TreeViewItem root)
        {
            var rows = GetRows() ?? new List<TreeViewItem>(200);

            rows.Clear();
            if (!string.IsNullOrEmpty(searchString))
            {
                Search(m_Infos, searchString, rows);
            }
            else
            {
                var childItem = new TreeViewItem(m_Infos.id, -1, m_Infos.name);
                root.AddChild(childItem);
                rows.Add(childItem);
                if (IsExpanded(childItem.id))
                {
                    AddChildrenRecursive(m_Infos, childItem, rows);
                }
                else
                {
                    childItem.children = CreateChildListForCollapsedParent();
                }
            }

            SetupDepthsFromParentsAndChildren(root);
            return rows;
        }

        private void AddChildrenRecursive(VirtualFolderInfo info, TreeViewItem item, IList<TreeViewItem> rows)
        {
            if (info.children == null)
            {
                return;
            }

            int childCount = info.children.Count;
            item.children = new List<TreeViewItem>(childCount);
            for (int i = 0; i < childCount; ++i)
            {
                var childInfo = info.children[i];
                var childItem = new TreeViewItem(childInfo.id, -1, childInfo.name);
                item.AddChild(childItem);
                rows.Add(childItem);

                if (childInfo.childCount > 0)
                {
                    if (IsExpanded(childItem.id))
                    {
                        AddChildrenRecursive(childInfo, childItem, rows);
                    }
                    else
                    {
                        childItem.children = CreateChildListForCollapsedParent();
                    }
                }
            }
        }

        private void Search(VirtualFolderInfo searchFromThis, string search, IList<TreeViewItem> result)
        {
            if (string.IsNullOrEmpty(search))
                throw new ArgumentException("Invalid search: cannot be null or empty", "search");

            int itemDepth = 0; // tree is flattened when searching

            Stack<VirtualFolderInfo> stack = new Stack<VirtualFolderInfo>();
            foreach (var element in searchFromThis.children)
                stack.Push((VirtualFolderInfo)element);
            while (stack.Count > 0)
            {
                VirtualFolderInfo current = stack.Pop();
                // Matches search?
                if (current.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(new TreeViewItem(current.id, itemDepth, current.name));
                }

                if (current.children != null && current.children.Count > 0)
                {
                    foreach (var element in current.children)
                    {
                        stack.Push((VirtualFolderInfo)element);
                    }
                }
            }
        }

        protected override IList<int> GetAncestors(int id)
        {
            var info = m_Infos.Find(id);

            List<int> ancestors = new List<int>();
            while (info.parent != null)
            {
                ancestors.Add(info.parent.id);
                info = info.parent;
            }

            return ancestors;
        }

        protected override IList<int> GetDescendantsThatHaveChildren(int id)
        {
            Stack<VirtualFolderInfo> stack = new Stack<VirtualFolderInfo>();

            var start = m_Infos.Find(id);
            stack.Push(start);

            var parents = new List<int>();
            while (stack.Count > 0)
            {
                VirtualFolderInfo current = stack.Pop();
                parents.Add(current.id);
                for (int i = 0; i < current.childCount; ++i)
                {
                    if (current.childCount > 0)
                        stack.Push(current.children[i]);
                }
            }

            return parents;
        }

        protected override bool CanRename(TreeViewItem item)
        {
            Rect renameRect = GetRenameRect(treeViewRect, 0, item);
            return renameRect.width > 30;
        }

        protected override void RenameEnded(RenameEndedArgs args)
        {
            if (args.acceptedRename)
            {
                var info = m_Infos.Find(args.itemID);
                info.name = args.newName;
                Reload();
            }
        }

        protected override bool CanMultiSelect(TreeViewItem item)
        {
            return false;
        }

        const string kGenericDragId = "GenericDragColumnDragging";
        const string kIsFolderGenericData = "IsFolder";

        protected override bool CanStartDrag(CanStartDragArgs args)
        {
            return true;
        }

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
        {
            if (hasSearch)
            {
                return;
            }

            DragAndDrop.PrepareStartDrag();
            var draggedRows = GetRows().Where(item => args.draggedItemIDs.Contains(item.id)).ToList();
            DragAndDrop.SetGenericData(kGenericDragId, draggedRows);
            DragAndDrop.objectReferences = new Object[] { };
            string title = draggedRows.Count == 1 ? draggedRows[0].displayName : "< Multiple >";
            DragAndDrop.StartDrag(title);
        }

        protected override DragAndDropVisualMode HandleDragAndDrop(DragAndDropArgs args)
        {
            if (DragAndDrop.GetGenericData(kIsFolderGenericData) as string == "isFolder")
            {
                if (args.dragAndDropPosition == DragAndDropPosition.UponItem)
                {
                    if (args.performDrop)
                    {
                        VirtualFolderInfo info = m_Infos.Find(args.parentItem.id);
                        info.path = DragAndDrop.paths[0];
                    }
                    return DragAndDropVisualMode.Link;
                }
                return DragAndDropVisualMode.None;
            }

            var draggedRows = DragAndDrop.GetGenericData(kGenericDragId) as List<TreeViewItem>;
            if (draggedRows == null)
            {
                return DragAndDropVisualMode.None;
            }

            switch (args.dragAndDropPosition)
            {
                case DragAndDropPosition.UponItem:
                case DragAndDropPosition.BetweenItems:
                    {
                        bool validDrag = ValidDrag(args.parentItem, draggedRows);
                        if (args.performDrop && validDrag)
                        {
                            OnDropDraggedElementsAtIndex(draggedRows, args.parentItem, args.insertAtIndex == -1 ? 0 : args.insertAtIndex);
                        }
                        return validDrag ? DragAndDropVisualMode.Move : DragAndDropVisualMode.None;
                    }

                case DragAndDropPosition.OutsideItems:
                    {
                        return DragAndDropVisualMode.None;
                    }
                default:
                    Debug.LogError("Unhandled enum " + args.dragAndDropPosition);
                    return DragAndDropVisualMode.None;
            }
        }

        private void OnDropDraggedElementsAtIndex(List<TreeViewItem> draggedRows, TreeViewItem parent, int insertIndex)
        {
            if (parent == rootItem)
            {
                return;
            }

            var selectedIDs = draggedRows.Select(x => x.id).ToArray();

            var draggedElements = new List<VirtualFolderInfo>();
            foreach (var x in draggedRows)
            {
                draggedElements.Add(m_Infos.Find(x.id));
            }

            VirtualFolderInfo parentElement = m_Infos.Find(parent.id);

            foreach (var draggedItem in draggedElements)
            {
                draggedItem.parent.children.Remove(draggedItem);    // remove from old parent
                draggedItem.parent = parentElement;					// set new parent
            }

            if (parentElement.children == null)
            {
                parentElement.children = new List<VirtualFolderInfo>();
            }

            insertIndex = Mathf.Min(parentElement.childCount, insertIndex);
            insertIndex = Mathf.Max(0, insertIndex);
            parentElement.children.InsertRange(insertIndex, draggedElements);

            Reload();
            SetSelection(selectedIDs, TreeViewSelectionOptions.RevealAndFrame);
        }

        private bool ValidDrag(TreeViewItem parent, List<TreeViewItem> draggedItems)
        {
            TreeViewItem currentParent = parent;
            while (currentParent != null)
            {
                if (draggedItems.Contains(currentParent))
                    return false;
                currentParent = currentParent.parent;
            }
            return true;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            VirtualFolderInfo info = m_Infos.Find(args.item.id);
            Rect rect = args.rowRect;
            rect.xMin = rect.xMax - 150f;
            if (Event.current.rawType == EventType.Repaint)
            {
                lineStyle.Draw(rect, Path.GetFileNameWithoutExtension(info.path), false, false, false, args.focused);
            }

            base.RowGUI(args);

            if (onGUIRowCallback != null)
            {
                onGUIRowCallback(info, args.rowRect, this);
            }
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            foreach (var selectedId in selectedIds)
            {
                FrameItemById(selectedId, false);
            }
        }

        private void SelectFolder(int instanceId)
        {
            Type projectBrowserType = Type.GetType("UnityEditor.ProjectBrowser,UnityEditor");
            if (projectBrowserType != null)
            {
                FieldInfo lastProjectBrowser = projectBrowserType.GetField("s_LastInteractedProjectBrowser", BindingFlags.Static | BindingFlags.Public);
                if (lastProjectBrowser != null)
                {
                    object lastProjectBrowserInstance = lastProjectBrowser.GetValue(null);
                    FieldInfo projectBrowserViewMode = projectBrowserType.GetField("m_ViewMode", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (projectBrowserViewMode != null)
                    {
                        // 0 - one column, 1 - two column
                        int viewMode = (int)projectBrowserViewMode.GetValue(lastProjectBrowserInstance);
                        if (viewMode == 1)
                        {
                            MethodInfo showFolderContents = projectBrowserType.GetMethod("ShowFolderContents", BindingFlags.NonPublic | BindingFlags.Instance);
                            if (showFolderContents != null)
                            {
                                showFolderContents.Invoke(lastProjectBrowserInstance, new object[] { instanceId, true });
                            }
                            else
                            {
                                Debug.LogError("Can't find ShowFolderContents method!");
                            }
                        }
                    }
                    else
                    {
                        Debug.LogError("Can't find m_ViewMode field!");
                    }
                }
                else
                {
                    Debug.LogError("Can't find s_LastInteractedProjectBrowser field!");
                }
            }
            else
            {
                Debug.LogError("Can't find UnityEditor.ProjectBrowser type!");
            }
        }

        public TreeViewItem GetItemById(int id)
        {
            var rows = GetRows();
            foreach (var row in rows)
            {
                if (row.id == id)
                {
                    return row;
                }
            }
            return null;
        }

        public void FrameItemById(int id, bool frame)
        {
            VirtualFolderInfo info = m_Infos.Find(id);
            if (!string.IsNullOrEmpty(info.path))
            {
                Object obj = AssetDatabase.LoadAssetAtPath<Object>(info.path);
                SelectFolder(obj.GetInstanceID());

                if (frame)
                {
                    EditorGUIUtility.PingObject(obj);
                }
            }
        }
    }
}