using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Graphs;
using Microsoft.Diagnostics.Symbols;
using Microsoft.Diagnostics.Tracing.Etlx;

namespace PerfView.PerfViewData
{
    /// <summary>
    /// A simple helper class that looks up symbols for Project N GCDumps 
    /// </summary>
    internal class TypeNameSymbolResolver
    {
        public enum TypeNameOptions
        {
            None,
            StripModuleName,
        }

        /// <summary>
        /// Create a new symbol resolver.  You give it a context file path (PDBS are looked up next to this if non-null) and
        /// a text writer in which to write symbol diagnostic messages.  
        /// </summary>
        public TypeNameSymbolResolver(string contextFilePath, TextWriter log) { m_contextFilePath = contextFilePath; m_log = log; }

        public string ResolveTypeName(int rvaOfType, TraceModuleFile module, TypeNameOptions options = TypeNameOptions.None)
        {
            Module mod = new Module(module.ImageBase);
            mod.BuildTime = module.BuildTime;
            mod.Path = module.FilePath;
            mod.PdbAge = module.PdbAge;
            mod.PdbGuid = module.PdbSignature;
            mod.PdbName = module.PdbName;
            mod.Size = module.ImageSize;

            string typeName = ResolveTypeName(rvaOfType, mod);

            // Trim the module from the type name if requested.
            if (options == TypeNameOptions.StripModuleName && !string.IsNullOrEmpty(typeName))
            {
                // Strip off the module name if present.
                string[] typeNameParts = typeName.Split(new char[] { '!' }, 2);
                if (typeNameParts.Length == 2)
                {
                    typeName = typeNameParts[1];
                }
            }

            return typeName;
        }

        public string ResolveTypeName(int typeID, Graphs.Module module)
        {
            if (module == null || module.Path == null)
            {
                m_log.WriteLine("Error: null module looking up typeID  0x{0:x}", typeID);
                return null;
            }
            if (module.PdbName == null || module.PdbGuid == Guid.Empty)
            {
                m_log.WriteLine("Error: module for typeID 0x{0:x} {1} does not have PDB signature info.", typeID, module.Path);
                return null;
            }
            if (module.PdbGuid == m_badPdb && m_badPdb != Guid.Empty)
            {
                return null;
            }

            if (m_pdbLookupFailures != null && m_pdbLookupFailures.ContainsKey(module.PdbGuid))  // TODO we are assuming unique PDB names (at least for failures). 
            {
                return null;
            }

            if (m_symReader == null)
            {
                m_symReader = App.GetSymbolReader(m_contextFilePath);
            }

            NativeSymbolModule symbolModule = null;
            var pdbPath = m_symReader.FindSymbolFilePath(module.PdbName, module.PdbGuid, module.PdbAge, module.Path);
            if (pdbPath != null)
            {
                symbolModule = m_symReader.OpenNativeSymbolFile(pdbPath);
            }
            else
            {
                if (m_pdbLookupFailures == null)
                {
                    m_pdbLookupFailures = new Dictionary<Guid, bool>();
                }

                m_pdbLookupFailures.Add(module.PdbGuid, true);
            }

            if (symbolModule == null)
            {
                m_numFailures++;
                if (m_numFailures <= 5)
                {
                    if (m_numFailures == 1 && !Path.GetFileName(module.Path).StartsWith("mrt", StringComparison.OrdinalIgnoreCase))
                    {
                        GuiApp.MainWindow.Dispatcher.BeginInvoke((Action)delegate ()
                        {
                            MessageBox.Show(GuiApp.MainWindow,
                                "Warning: Could not find PDB for module " + Path.GetFileName(module.Path) + "\r\n" +
                                "Some types will not have symbolic names.\r\n" +
                                "See log for more details.\r\n" +
                                "Fix by placing PDB on symbol path or in a directory called 'symbols' beside .gcdump file.",
                                "PDB lookup failure");
                        });
                    }
                    m_log.WriteLine("Failed to find PDB for module {0} to look up type 0x{1:x}", module.Path, typeID);
                    if (m_numFailures == 5)
                    {
                        m_log.WriteLine("Discontinuing PDB module lookup messages");
                    }
                }
                return null;
            }

            string typeName;
            try
            {
                typeName = symbolModule.FindNameForRva((uint)typeID);
            }
            catch (OutOfMemoryException)
            {
                // TODO find out why this happens?   I think this is because we try to do a ReadRVA 
                m_log.WriteLine("Error: Caught out of memory exception on file " + symbolModule.SymbolFilePath + ".   Skipping.");
                m_badPdb = module.PdbGuid;
                return null;
            }

            typeName = typeName.Replace(@"::`vftable'", "");
            typeName = typeName.Replace(@"::", ".");
            typeName = typeName.Replace(@"EEType__", "");
            typeName = typeName.Replace(@".Boxed_", ".");

            return typeName;
        }

        #region private 
        private TextWriter m_log;
        private string m_contextFilePath;
        private SymbolReader m_symReader;
        private int m_numFailures;
        private Guid m_badPdb;        // If we hit a bad PDB remember it to avoid logging too much 
        private Dictionary<Guid, bool> m_pdbLookupFailures;
        #endregion
    }
}