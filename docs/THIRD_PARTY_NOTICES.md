# Third-party notices

## v0.10 package scope

The manager-only preview package contains no Mihomo executable or geodata files.
The locally used core self-reports v1.19.24; this is not provenance verification.
The manager's original code, scripts and documentation are licensed under
GPL-3.0-only. Copyright (c) 2026 Rinko. See LICENSE for the complete terms.
Newly generated packages include the corresponding manager source and build
scripts in source/ (src/, tests/ and scripts/). This does not relicense any third-party component or
declare this preview a completed public release. Earlier packages have not
been retroactively updated and must not be reused as public release artifacts.
Before publishing a full runtime bundle, audit the exact core and geodata assets,
their provenance and applicable redistribution terms. See RELEASE_CHECKLIST.md.

## Manager-only dependency inventory (2026-10-16)

The build script compiles the project-owned C# sources and references the
system .NET Framework assemblies System, System.Core, System.Drawing,
System.Management, System.Web.Extensions and System.Windows.Forms. These system
assemblies are not copied into the ZIP. Since v1.1.0, YamlDotNet 16.3.0 (net47)
is included under the MIT license. Its exact binary hash is pinned in build and
release checks; its original license, source URL and provenance are shipped in
third_party/YamlDotNet/ and source/third_party/YamlDotNet/. Upstream source:
https://github.com/aaubry/YamlDotNet/tree/v16.3.0 . No geodata is bundled.
This inventory describes package contents,
not a legal opinion or an audit of user-supplied runtime components.

## Mihomo runtime (not bundled)

This project can run an unmodified Mihomo executable as a separate process and
control it through its local HTTP API.

- Upstream: https://github.com/MetaCubeX/mihomo/tree/Meta
- License: GNU General Public License v3.0
- Documentation: https://wiki.metacubex.one/

Release packages that include the executable must also include its license,
identify the exact upstream version, and provide the corresponding source-code
location or other materials required by the upstream license.

The upstream project also asks unaffiliated downstream projects not to include
the word `mihomo` in their project names.
