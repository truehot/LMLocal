using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LMLocal.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    internal interface ISolutionFileProvider
    {
        IEnumerable<string> GetFiles(bool includeProjects = false);
    }

    internal class SolutionFileProvider : ISolutionFileProvider
    {
        private readonly IVsSolution _solution;

        public SolutionFileProvider(IVsSolution solution)
        {
            _solution = solution;
        }

        public IEnumerable<string> GetFiles(bool includeProjects = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<string>();

            if (_solution == null)
                return result;

            Guid guid = Guid.Empty;
            if (_solution.GetProjectEnum((uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION, ref guid, out IEnumHierarchies enumHier) != VSConstants.S_OK)
                return result;

            IVsHierarchy[] hier = new IVsHierarchy[1];

            while (enumHier.Next(1, hier, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                var h = hier[0];
                if (h == null) continue;

                if (includeProjects && h is IVsProject vsProject)
                {
                    if (vsProject.GetMkDocument(VSConstants.VSITEMID_ROOT, out string projectPath) == VSConstants.S_OK &&
                        !string.IsNullOrEmpty(projectPath) && File.Exists(projectPath))
                    {
                        result.Add(projectPath);
                    }
                }

                result.AddRange(EnumerateHierarchyItems(h, VSConstants.VSITEMID_ROOT));
            }

            return result;
        }

        private IEnumerable<string> EnumerateHierarchyItems(IVsHierarchy hierarchy, uint itemId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var result = new List<string>();
            var stack = new Stack<uint>();
            stack.Push(itemId);

            while (stack.Count > 0)
            {
                uint currentId = stack.Pop();

                if (hierarchy.GetProperty(currentId, (int)__VSHPROPID.VSHPROPID_FirstChild, out object childObj) != VSConstants.S_OK)
                    continue;

                if (!TryGetItemId(childObj, out uint childId))
                    continue;

                while (childId != VSConstants.VSITEMID_NIL)
                {
                    if (hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_FirstChild, out object maybeChild) == VSConstants.S_OK
                        && TryGetItemId(maybeChild, out uint anyChild) && anyChild != VSConstants.VSITEMID_NIL)
                    {
                        stack.Push(childId);
                    }
                    else
                    {
                        string filePath = GetFilePath(hierarchy, childId);
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            result.Add(filePath);
                        }
                    }

                    if (hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_NextSibling, out object nextObj) != VSConstants.S_OK)
                        break;

                    if (!TryGetItemId(nextObj, out childId))
                        break;
                }
            }

            return result;
        }

        private bool TryGetItemId(object value, out uint id)
        {
            id = VSConstants.VSITEMID_NIL;
            if (value == null) return false;

            switch (value)
            {
                case uint u:
                    id = u; return true;
                case int i when i >= 0:
                    id = Convert.ToUInt32(i); return true;
                case long l when l >= 0:
                    id = Convert.ToUInt32(l); return true;
                case short s when s >= 0:
                    id = Convert.ToUInt32(s); return true;
                case byte b:
                    id = b; return true;
                case IntPtr p:
                    {
                        long v;
                        try
                        {
                            v = p.ToInt64();
                        }
                        catch
                        {
                            return false;
                        }

                        if (v >= 0 && v <= uint.MaxValue)
                        {
                            id = (uint)v;
                            return true;
                        }
                        return false;
                    }
                default:
                    if (value is IConvertible conv)
                    {
                        try
                        {
                            decimal d = conv.ToDecimal(CultureInfo.InvariantCulture);
                            if (d >= 0m && d <= uint.MaxValue)
                            {
                                if (decimal.Truncate(d) == d)
                                {
                                    id = (uint)d;
                                    return true;
                                }
                            }
                        }
                        catch
                        {
                            InternalLogger.Warn($"TryGetItemId: failed to convert value of type {value.GetType().FullName} to uint");
                        }
                    }
                    return false;
            }
        }

        private string GetFilePath(IVsHierarchy hierarchy, uint itemId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_IsNonMemberItem, out object isNonMemberObj) == VSConstants.S_OK)
            {
                if (isNonMemberObj is bool isNonMember && isNonMember)
                    return null;
            }

            if (hierarchy is IVsProject vsProject)
            {
                if (vsProject.GetMkDocument(itemId, out string mkDocument) == VSConstants.S_OK && !string.IsNullOrEmpty(mkDocument))
                {
                    if (FileExistsSafe(mkDocument, out string existing))
                        return existing;
                }
            }

            if (hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_SaveName, out object saveNameObj) == VSConstants.S_OK)
            {
                var saveName = saveNameObj as string;
                if (!string.IsNullOrEmpty(saveName))
                {
                    if (hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ProjectDir, out object projDirObj) == VSConstants.S_OK)
                    {
                        var projectDir = projDirObj as string;
                        if (!string.IsNullOrEmpty(projectDir))
                        {
                            try
                            {
                                var full = Path.Combine(projectDir, saveName);
                                if (FileExistsSafe(full, out string existing))
                                    return existing;
                            }
                            catch (ArgumentException ex)
                            {
                                InternalLogger.Warn($"GetFilePath: malformed path components for projectDir='{projectDir}' saveName='{saveName}': {ex.Message}");
                            }
                        }
                    }
                }
            }

            return null;
        }
        private static bool FileExistsSafe(string path, out string existingPath)
        {
            existingPath = null;
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                if (File.Exists(path))
                {
                    existingPath = path;
                    return true;
                }
            }
            catch (Exception ex) when (ex is ArgumentException
                                       || ex is NotSupportedException
                                       || ex is PathTooLongException
                                       || ex is IOException
                                       || ex is UnauthorizedAccessException
                                       || ex is System.Security.SecurityException)
            {
                InternalLogger.Warn($"FileExistsSafe: error probing path '{path}': {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }
    }
}
