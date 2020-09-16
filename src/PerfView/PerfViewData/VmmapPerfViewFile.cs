using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    internal class VmmapPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Vmmap data file"; } }
        public override string[] FileExtensions { get { return new string[] { ".mmp" }; } }


        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            using (Stream dataStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                var xmlStream = dataStream;
                XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
                using (XmlReader reader = XmlTextReader.Create(xmlStream, settings))
                {
                    return new VMMapStackSource(reader);
                }
            }
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            var defaultFold = "^Block;^Shareable;^Image Section;^Heap;^Private data;^Thread Stack";
            stackWindow.FoldRegExTextBox.Items.Insert(0, "^Images in");
            stackWindow.FoldRegExTextBox.Items.Insert(0, defaultFold);

            stackWindow.IncludeRegExTextBox.Items.Insert(0, "^Images in;^MappedFiles in");
            stackWindow.IncludeRegExTextBox.Items.Insert(0, "^Block Private");

            stackWindow.GroupRegExTextBox.Items.Insert(0, "[group files] ^MappedFile{*}->Image$1;^Image{*}->Image$1;Group MappedFile->Group Image;Group Image->Group Image");
        }
        protected internal override void FirstAction(StackWindow stackWindow)
        {
            stackWindow.CallTreeTab.IsSelected = true;
        }

        #region private
        [Flags]
        private enum PageProtection
        {
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
            PAGE_EXECUTE_WRITECOPY = 0x80,
            PAGE_NOACCESS = 0x01,
            PAGE_READONLY = 0x02,
            PAGE_READWRITE = 0x04,
            PAGE_WRITECOPY = 0x08,
        }

        private enum UseType
        {
            Heap = 0,
            Stack = 1,
            Image = 2,
            MappedFile = 3,
            PrivateData = 4,
            Shareable = 5,
            Free = 6,
            // Unknown1 = 7,
            ManagedHeaps = 8,
            // Unknown2 = 9,
            Unusable = 10,
        }

        private class MemoryNode
        {
            public static MemoryNode Root()
            {
                var ret = new MemoryNode();
                ret.Size = ulong.MaxValue;
                ret.Details = "[ROOT]";
                return ret;
            }
            public MemoryNode Add(XmlReader reader)
            {
                var newNode = new MemoryNode(reader);
                Insert(newNode);
                return newNode;
            }

            public ulong End { get { return Address + Size; } }
            public ulong Address;
            public ulong Blocks;
            public ulong ShareableWS;
            public ulong SharedWS;
            public ulong Size;
            public ulong Commit;
            public ulong PrivateBytes;
            public ulong PrivateWS;
            public ulong Id;
            public PageProtection Protection;
            public ulong Storage;
            public UseType UseType;
            public string Type;
            public string Details;
            public List<MemoryNode> Children;
            public MemoryNode Parent;

            public override string ToString()
            {
                return string.Format("<MemoryNode Name=\"{0}\" Start=\"0x{1:x}\" Length=\"0x{2:x}\"/>", Details, Address, Size);
            }

            #region private

            private void Insert(MemoryNode newNode)
            {
                Debug.Assert(Address <= newNode.Address && newNode.End <= End);
                if (Children == null)
                {
                    Children = new List<MemoryNode>();
                }

                // Search backwards for efficiency.  
                for (int i = Children.Count; 0 < i;)
                {
                    var child = Children[--i];
                    if (child.Address <= newNode.Address && newNode.End <= child.End)
                    {
                        child.Insert(newNode);
                        return;
                    }
                }
                Children.Add(newNode);
                newNode.Parent = this;
            }
            private MemoryNode() { }
            private MemoryNode(XmlReader reader)
            {
                Address = FetchLong(reader, "Address");
                Blocks = FetchLong(reader, "Blocks");
                ShareableWS = FetchLong(reader, "ShareableWS");
                SharedWS = FetchLong(reader, "SharedWS");
                Size = FetchLong(reader, "Size");
                Commit = FetchLong(reader, "Commit");
                PrivateBytes = FetchLong(reader, "PrivateBytes");
                PrivateWS = FetchLong(reader, "PrivateWS");
                Id = FetchLong(reader, "Id");     // This identifies the heap (for Heap type data)
                Protection = (PageProtection)int.Parse(reader.GetAttribute("Protection") ?? "0");
                Storage = FetchLong(reader, "Storage");
                UseType = (UseType)int.Parse(reader.GetAttribute("UseType") ?? "0");
                Type = reader.GetAttribute("Type") ?? "";
                Details = reader.GetAttribute("Details") ?? "";
            }

            private static ulong FetchLong(XmlReader reader, string attributeName)
            {
                ulong ret = 0L;
                var attrValue = reader.GetAttribute(attributeName);
                if (attrValue != null)
                {
                    ulong.TryParse(attrValue, out ret);
                }

                return ret;
            }
            #endregion
        }

        private class VMMapStackSource : InternStackSource
        {
            public VMMapStackSource(XmlReader reader)
            {
                m_sample = new StackSourceSample(this);
                MemoryNode top = MemoryNode.Root();
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        // Start over if we see another snapshot.  THus we read the last one.   
                        // THis is present VMMAP behavior.  TODO We should think about doing better.   
                        if (reader.Name == "Snapshot")
                        {
                            top = MemoryNode.Root();
                        }
                        else if (reader.Name == "Region")
                        {
                            top.Add(reader);
                        }
                    }
                }

                foreach (var child in top.Children)
                {
                    AddToSource(child, StackSourceCallStackIndex.Invalid);
                }

                Interner.DoneInterning();
            }

            /// <summary>
            /// Add all the nodes represented by 'node' to the source.  'parentStack' is the
            /// stack that represents the parent of 'node' (thus the top node is Invalid, 
            /// which represents the empty stack)
            /// </summary>
            private void AddToSource(MemoryNode node, StackSourceCallStackIndex parentStack)
            {
                if (node.Children != null)
                {
                    // At the topmost level we have group (UseType), for the node
                    if (parentStack == StackSourceCallStackIndex.Invalid)
                    {
                        parentStack = AddFrame("Group " + node.UseType.ToString(), parentStack);
                    }

                    if (node.Details.Length != 0)
                    {
                        // Group directories together.  
                        if (node.UseType == UseType.Image || node.UseType == UseType.MappedFile)
                        {
                            parentStack = AddDirPathNodes(node.Details, parentStack, false, node.UseType);
                        }
                        else
                        {
                            parentStack = AddFrame(node.Details, parentStack);
                        }
                    }

                    foreach (var child in node.Children)
                    {
                        AddToSource(child, parentStack);
                    }

                    return;
                }

                var details = node.Details;
                if (node.UseType == UseType.Image && details.Length != 0)
                {
                    details = "Image Section " + details;
                }

                if (details.Length == 0)
                {
                    details = node.Type;
                }

                var frameName = string.Format("{0,-20} address 0x{1:x} size 0x{2:x}", details, node.Address, node.Size);
                StackSourceCallStackIndex nodeStack = AddFrame(frameName, parentStack);

                if (node.PrivateWS != 0)
                {
                    AddSample("Block Private", node.PrivateWS, nodeStack);
                }

                if (node.ShareableWS != 0)
                {
                    AddSample("Block Sharable", node.ShareableWS, nodeStack);
                }
            }
            /// <summary>
            /// Adds nodes for each parent directory that has more than one 'child' (its count is different than it child) 
            /// </summary>
            private StackSourceCallStackIndex AddDirPathNodes(string path, StackSourceCallStackIndex parentStack, bool isDir, UseType useType)
            {
                var lastBackslashIdx = path.LastIndexOf('\\');
                if (lastBackslashIdx >= 0)
                {
                    var dir = path.Substring(0, lastBackslashIdx);
                    parentStack = AddDirPathNodes(dir, parentStack, true, useType);
                }

                var kindName = (useType == UseType.MappedFile) ? "MappedFile" : "Image";
                var prefix = isDir ? kindName + "s in" : kindName;
                return AddFrame(prefix + " " + path, parentStack);
            }
            private void AddSample(string memoryKind, ulong metric, StackSourceCallStackIndex parentStack)
            {
                m_sample.Metric = metric / 1024F;
                var frameName = string.Format("{0,-15} {1}K WS", memoryKind, metric / 1024);
                m_sample.StackIndex = AddFrame(frameName, parentStack);
                AddSample(m_sample);
            }
            private StackSourceCallStackIndex AddFrame(string frameName, StackSourceCallStackIndex parentStack)
            {
                var moduleIdx = Interner.ModuleIntern("");
                var frameIdx = Interner.FrameIntern(frameName, moduleIdx);
                return Interner.CallStackIntern(frameIdx, parentStack);
            }

            private StackSourceSample m_sample;

        }
        #endregion
    }
}