using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VirtualFolder
{
    [Serializable]
    public class VirtualFolderInfo
    {
        public int id;
        public string name;
        public int depth;
        public string path;
        [NonSerialized]
        public List<VirtualFolderInfo> children;

        public VirtualFolderInfo parent { get; set; }

        public int childCount
        {
            get
            {
                if (children != null)
                {
                    return children.Count;
                }
                return 0;
            }
        }

        public VirtualFolderInfo(string folderName, VirtualFolderInfo parentInfo = null)
        {
            name = folderName;
            parent = parentInfo;
            id = GUID.Generate().ToString().GetHashCode();
            depth = -1;
            if (parent != null)
            {
                depth = parent.depth + 1;
            }
        }

        public void SetChildrenParent()
        {
            if (children != null)
            {
                foreach (var child in children)
                {
                    child.parent = this;
                    child.SetChildrenParent();
                }
            }
        }

        public VirtualFolderInfo Find(int itemId)
        {
            if (id == itemId)
            {
                return this;
            }

            if (children != null)
            {
                foreach (var child in children)
                {
                    VirtualFolderInfo info = child.Find(itemId);
                    if (info != null)
                    {
                        return info;
                    }
                }
            }
            return null;
        }

        public int AddChild()
        {
            if (children == null)
            {
                children = new List<VirtualFolderInfo>();
            }
            children.Add(new VirtualFolderInfo("New Folder", this));
            return children[children.Count - 1].id;
        }

        public void RemoveChild(VirtualFolderInfo info)
        {
            if (children != null)
            {
                info.parent = null;
                children.Remove(info);
            }
        }
    }

    [Serializable]
    public class VirtualFolderList
    {
        [Serializable]
        public class VirtualFolderSubList
        {
            public List<VirtualFolderInfo> infos;

            public VirtualFolderSubList(List<VirtualFolderInfo> list)
            {
                infos = list;
            }
        }

        public List<VirtualFolderSubList> allInfos;

        [NonSerialized]
        public List<VirtualFolderInfo> rootList;

        public int rootCount
        {
            get
            {
                if (rootList != null)
                {
                    return rootList.Count;
                }
                return 0;
            }
        }

        public void AddRoot(string name)
        {
            if (rootList == null)
            {
                rootList = new List<VirtualFolderInfo>();
            }
            rootList.Add(new VirtualFolderInfo(name));
        }

        public bool ContainsRoot(string name)
        {
            if (rootList != null)
            {
                for (int i = 0; i < rootList.Count; i++)
                {
                    if (rootList[i].name == name)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public string SaveToString()
        {
            if (allInfos == null)
            {
                allInfos = new List<VirtualFolderSubList>();
            }
            allInfos.Clear();
            foreach (var info in rootList)
            {
                List<VirtualFolderInfo> infoList = new List<VirtualFolderInfo>();
                VirtualFolderUtility.UpdateDepthValues(info);
                VirtualFolderUtility.TreeToList(info, infoList);
                allInfos.Add(new VirtualFolderSubList(infoList));
            }
            return JsonUtility.ToJson(this);
        }

        private void LoadFromString()
        {
            if (rootList == null)
            {
                rootList = new List<VirtualFolderInfo>();
            }
            rootList.Clear();
            foreach (var listInfo in allInfos)
            {
                rootList.Add(VirtualFolderUtility.ListToTree(listInfo.infos));
            }
        }

        public static VirtualFolderList CreateFromString(string jsonString)
        {
            VirtualFolderList list = JsonUtility.FromJson<VirtualFolderList>(jsonString);
            list.LoadFromString();
            return list;
        }
    }

    public static class VirtualFolderUtility
    {
        public static void TreeToList<T>(T root, IList<T> result) where T : VirtualFolderInfo
        {
            if (result == null)
                throw new NullReferenceException("The input 'IList<T> result' list is null");
            result.Clear();

            Stack<T> stack = new Stack<T>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                T current = stack.Pop();
                result.Add(current);

                if (current.children != null && current.children.Count > 0)
                {
                    for (int i = current.children.Count - 1; i >= 0; i--)
                    {
                        stack.Push((T)current.children[i]);
                    }
                }
            }
        }

        // Returns the root of the tree parsed from the list (always the first element).
        // Important: the first item and is required to have a depth value of -1. 
        // The rest of the items should have depth >= 0. 
        public static T ListToTree<T>(IList<T> list) where T : VirtualFolderInfo
        {
            // Validate input
            ValidateDepthValues(list);

            // Clear old states
            foreach (var element in list)
            {
                element.parent = null;
                element.children = null;
            }

            // Set child and parent references using depth info
            for (int parentIndex = 0; parentIndex < list.Count; parentIndex++)
            {
                var parent = list[parentIndex];
                bool alreadyHasValidChildren = parent.children != null;
                if (alreadyHasValidChildren)
                    continue;

                int parentDepth = parent.depth;
                int childCount = 0;

                // Count children based depth value, we are looking at children until it's the same depth as this object
                for (int i = parentIndex + 1; i < list.Count; i++)
                {
                    if (list[i].depth == parentDepth + 1)
                        childCount++;
                    if (list[i].depth <= parentDepth)
                        break;
                }

                // Fill child array
                List<VirtualFolderInfo> childList = null;
                if (childCount != 0)
                {
                    childList = new List<VirtualFolderInfo>(childCount); // Allocate once
                    childCount = 0;
                    for (int i = parentIndex + 1; i < list.Count; i++)
                    {
                        if (list[i].depth == parentDepth + 1)
                        {
                            list[i].parent = parent;
                            childList.Add(list[i]);
                            childCount++;
                        }

                        if (list[i].depth <= parentDepth)
                            break;
                    }
                }

                parent.children = childList;
            }

            return list[0];
        }

        // Check state of input list
        public static void ValidateDepthValues<T>(IList<T> list) where T : VirtualFolderInfo
        {
            if (list.Count == 0)
                throw new ArgumentException("list should have items, count is 0, check before calling ValidateDepthValues", "list");

            if (list[0].depth != -1)
                throw new ArgumentException("list item at index 0 should have a depth of -1 (since this should be the hidden root of the tree). Depth is: " + list[0].depth, "list");

            for (int i = 0; i < list.Count - 1; i++)
            {
                int depth = list[i].depth;
                int nextDepth = list[i + 1].depth;
                if (nextDepth > depth && nextDepth - depth > 1)
                    throw new ArgumentException(string.Format("Invalid depth info in input list. Depth cannot increase more than 1 per row. Index {0} has depth {1} while index {2} has depth {3}", i, depth, i + 1, nextDepth));
            }

            for (int i = 1; i < list.Count; ++i)
                if (list[i].depth < 0)
                    throw new ArgumentException("Invalid depth value for item at index " + i + ". Only the first item (the root) should have depth below 0.");

            if (list.Count > 1 && list[1].depth != 0)
                throw new ArgumentException("Input list item at index 1 is assumed to have a depth of 0", "list");
        }

        // For updating depth values below any given element e.g after reparenting elements
        public static void UpdateDepthValues<T>(T root) where T : VirtualFolderInfo
        {
            if (root == null)
                throw new ArgumentNullException("root", "The root is null");

            if (root.childCount <= 0)
                return;

            Stack<VirtualFolderInfo> stack = new Stack<VirtualFolderInfo>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                VirtualFolderInfo current = stack.Pop();
                if (current.children != null)
                {
                    foreach (var child in current.children)
                    {
                        child.depth = current.depth + 1;
                        stack.Push(child);
                    }
                }
            }
        }
    }
}