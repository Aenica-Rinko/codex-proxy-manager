# Third-party notices

## v0.10 package scope

The manager-only preview package contains no Mihomo executable or geodata files.
The locally used core self-reports v1.19.24; this is not provenance verification.
The manager's original code, scripts and documentation are licensed under
GPL-3.0-only. Copyright (c) 2026 Rinko. See LICENSE for the complete terms.
Newly generated packages include the corresponding manager source and build
scripts in manager/. This does not relicense any third-party component or
declare this preview a completed public release. Earlier packages have not
been retroactively updated and must not be reused as public release artifacts.
Before publishing a full runtime bundle, audit the exact core and geodata assets,
their provenance and applicable redistribution terms. See RELEASE_CHECKLIST.md.

## Mihomo

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
