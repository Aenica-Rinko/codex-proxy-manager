// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
// This program is free software: you can redistribute it and/or modify it
// under the terms of version 3 of the GNU General Public License.
// This program is distributed WITHOUT ANY WARRANTY; without even the implied
// warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
// See the LICENSE file distributed with this program for the full terms.
using System.Reflection;

[assembly: AssemblyTitle("Codex Proxy Manager")]
[assembly: AssemblyProduct("Codex Proxy Manager")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Rinko")]
[assembly: AssemblyVersion(CodexProxyManager.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(CodexProxyManager.AppInfo.Version + ".0")]

namespace CodexProxyManager
{
    internal static class AppInfo
    {
        internal const string Version = "0.10.2";
        internal const string DisplayVersion = "v" + Version;
        internal const string Title = "Codex Proxy Manager " + DisplayVersion;
    }
}
