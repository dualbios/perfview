using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;

namespace PerfView.PerfViewData
{
    public class PerfViewDirectory : PerfViewTreeItem
    {
        // Only names that match this filter are displayed. 
        public Regex Filter
        {
            get { return m_filter; }
            set
            {
                if (m_filter != value)
                {
                    m_filter = value;
                    m_Children = null;
                    FirePropertyChanged("Children");
                }
            }
        }
        public PerfViewDirectory(string path)
        {
            m_filePath = path;
            Name = System.IO.Path.GetFileName(path);
        }

        public override IList<PerfViewTreeItem> Children
        {
            get
            {
                if (m_Children == null)
                {
                    m_Children = new List<PerfViewTreeItem>();
                    if (Name != "..")
                    {
                        try
                        {
                            foreach (var filePath in FilesInDirectory(m_filePath))
                            {
                                var template = PerfViewFile.TryGet(filePath);
                                if (template != null)
                                {
                                    // Filter out kernel, rundown files etc, if the base file exists.  
                                    Match m = Regex.Match(filePath, @"^(.*)\.(kernel|clr|user)[^.]*\.etl$", RegexOptions.IgnoreCase);
                                    if (m.Success && File.Exists(m.Groups[1].Value + ".etl"))
                                        continue;

                                    // Filter out any items we were asked to filter out.  
                                    if (m_filter != null && !m_filter.IsMatch(Path.GetFileName(filePath)))
                                        continue;

                                    m_Children.Add(PerfViewFile.Get(filePath, template));
                                }
                            }

                            foreach (var dir in DirsInDirectory(m_filePath))
                            {
                                // Filter out any items we were asked to filter out.  
                                if (m_filter != null && !m_filter.IsMatch(Path.GetFileName(dir)))
                                {
                                    continue;
                                }
                                // We know that .NGENPDB directories are uninteresting, filter them out.  
                                if (dir.EndsWith(".NGENPDB", StringComparison.OrdinalIgnoreCase))
                                {
                                    continue;
                                }

                                m_Children.Add(new PerfViewDirectory(dir));
                            }

                            // We always have the parent directory.  
                            m_Children.Add(new PerfViewDirectory(System.IO.Path.Combine(m_filePath, "..")));
                        }
                        // FIX NOW review
                        catch (Exception) { }
                    }
                }
                return m_Children;
            }
        }
        public override string HelpAnchor { get { return null; } }      // Don't bother with help for this.  

        /// <summary>
        /// Open the file (This might be expensive (but maybe not).  This should populate the Children property 
        /// too.  
        /// </summary>
        public override void Open(Window parentWindow, StatusBar worker, Action doAfter)
        {
            var mainWindow = parentWindow as MainWindow;
            if (mainWindow != null)
            {
                mainWindow.OpenPath(FilePath);
            }

            doAfter?.Invoke();
        }
        /// <summary>
        /// Close the file
        /// </summary>
        public override void Close() { }

        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FolderOpenBitmapImage"] as ImageSource; } }

        #region private

        private class DirCacheEntry
        {
            public string[] FilesInDirectory;
            public string[] DirsInDirectory;
            public DateTime LastWriteTimeUtc;
        }

        // To speed things up we remember the list list of directory items we fetched from disk
        private static Dictionary<string, DirCacheEntry> s_dirCache = new Dictionary<string, DirCacheEntry>();

        private static string[] FilesInDirectory(string directoryPath)
        {
            var entry = GetDirEntry(directoryPath);
            if (entry.FilesInDirectory == null)
            {
                entry.FilesInDirectory = Directory.GetFiles(directoryPath);
            }

            return entry.FilesInDirectory;
        }

        private static string[] DirsInDirectory(string directoryPath)
        {
            var entry = GetDirEntry(directoryPath);
            if (entry.DirsInDirectory == null)
            {
                entry.DirsInDirectory = Directory.GetDirectories(directoryPath);
            }

            return entry.DirsInDirectory;
        }

        /// <summary>
        /// Gets a cache entry, nulls it out if it is out of date.  
        /// </summary>
        private static DirCacheEntry GetDirEntry(string directoryPath)
        {
            DateTime lastWrite = Directory.GetLastWriteTimeUtc(directoryPath);
            DirCacheEntry entry;
            if (!s_dirCache.TryGetValue(directoryPath, out entry))
            {
                s_dirCache[directoryPath] = entry = new DirCacheEntry();
            }

            if (lastWrite != entry.LastWriteTimeUtc)
            {
                entry.DirsInDirectory = null;
                entry.FilesInDirectory = null;
            }
            entry.LastWriteTimeUtc = lastWrite;
            return entry;
        }

        private Regex m_filter;
        #endregion
    }
}