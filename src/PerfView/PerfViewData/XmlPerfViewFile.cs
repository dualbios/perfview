using System;
using System.IO;
using System.Xml;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tracing.Stacks;
using PerfViewModel;

namespace PerfView.PerfViewData
{
    internal class XmlPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "PerfView XML FILE"; } }
        public override string[] FileExtensions { get { return new string[] { ".perfView.xml", ".perfView.xml.zip", ".perfView.json", ".perfView.json.zip" }; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            m_guiState = new StackWindowGuiState();

            return new XmlStackSource(FilePath, delegate (XmlReader reader)
            {
                if (reader.Name == "StackWindowGuiState")
                {
                    m_guiState = m_guiState.ReadFromXml(reader);
                }
                // These are only here for backward compatibility
                else if (reader.Name == "FilterXml")
                {
                    m_guiState.FilterGuiState.ReadFromXml(reader);
                }
                else if (reader.Name == "Log")
                {
                    m_guiState.Log = reader.ReadElementContentAsString().Trim();
                }
                else if (reader.Name == "Notes")
                {
                    m_guiState.Notes = reader.ReadElementContentAsString().Trim();
                }
                else
                {
                    reader.Read();
                }
            });
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            stackWindow.RestoreWindow(m_guiState, FilePath);
        }
        protected internal override void FirstAction(StackWindow stackWindow)
        {
            if (m_guiState != null)
            {
                stackWindow.GuiState = m_guiState;
            }

            m_guiState = null;
        }

        public override void LookupSymbolsForModule(string simpleModuleName, TextWriter log, int processId = 0)
        {
            throw new ApplicationException("Symbols can not be looked up after a stack view has been saved.\r\n" +
                                           "You must resolve all symbols you need before saving.\r\n" +
                                           "Consider the right click -> Lookup Warm Symbols command");
        }

        private StackWindowGuiState m_guiState;
    }
}