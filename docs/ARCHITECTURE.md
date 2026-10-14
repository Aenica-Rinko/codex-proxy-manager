# Architecture

Codex Proxy Manager is a Windows application-scoped proxy manager. It owns the
application lifecycle and routing policy while delegating proxy protocols to a
replaceable backend.

## Current components

- `LocalFileImport`: bounded strict text snapshot, source path not persisted, node-only parsing through an isolated Mihomo file provider with no target/data listeners, health checks or inherited CLASH hooks; private snapshot/scoped config and rollback of exact newly created files. Full original global policy and remote providers are not imported.

- `DashboardForm`: connection state, traffic, latency, and node switching UI.
- Dashboard polling runs on one background worker at a time, with copied controller credentials and revision checks before UI updates; stopped/switched sessions discard stale results. Health-check results also require the original session/node. "Entry available" is not an end-to-end login claim.
- `LocalControllerTransport`: shared loopback-only GET/PUT/DELETE transport; no inherited proxy or redirects, response cap, connection/read timeout and total wall-clock abort deadline.
- `UiButton` / `UiComboBox`: native keyboard/accessibility semantics with shared dark rendering.
- `PrivacyCenterForm`: live configuration/request state, screenshot masking, bounded log actions, explicit local browser behavior test.
- `PrivacyLog`: fixed event output only, size/age rotation, exact non-recursive log cleanup.
- `BrowserPrivacyProbe`: Chrome/Edge-only temporary profiles, loopback HTTP/STUN fixtures, bounded local CDP requests; never attach to the user's current browser or claim global leak protection.
- `ManagerContext`: UI/tray orchestration, user actions and revision-checked presentation; no core Process ownership, YAML session preparation or node REST implementation.
- `ProxySessionService`: owns the core, application launch/resume, private session config/state, scoped stale-core cleanup and watchdog startup. Stop is idempotent and deletes only state matching this service's owned session; idle services do not erase unknown sessions.
- `RecoveryWatchdog`: separate recovery component, preserving executable/start-time/session-token checks.
- `ControllerClient` / `IControllerClient`: immutable loopback endpoint/secret pair and injectable read/write boundary over the existing bounded transport.
- `NodeControlService` / `NodeHealthPolicy`: catalog parsing, selector policy, copied delay snapshots, split core/node operations and per-node consecutive failure tracking. A missing explicitly configured selector is an error, not permission to control another group.
- `DashboardPoller`: single-flight background collection with no Form/control references; the UI adapter still checks revisions before displaying results.
- `ProcessProxyLauncher`: installed MSIX package discovery and direct executable launch with Electron proxy flags plus child-only proxy environment variables.
- `RunningApplication`: bounded read-only WMI inspection of the exact executable's main processes; Windows argument parsing checks the same loopback proxy, bypass and privacy switches. Compatible Chromium/Electron sessions resume only the core, without target launch/termination. Raw process command lines stay in memory and are never logged or saved. Environment mode, unknown evidence, alternate proxy sources and mismatches fail closed.
- `PrivacyProtection`: per-app optional launch restrictions for WebRTC, browser host resolution and QUIC. Requests are not enforcement guarantees; Electron may ignore WebRTC switches. No global policies or firewall rules are written.
- `ApplicationConfiguration`: backward-compatible app registry, per-app profile bindings, executable validation and process-scoped session configuration.
- `ApplicationManagerForm`: app onboarding, edit, removal, and selection UI.
- `Diagnostics`: read-only state collection, process-filtered connection counts, bounded local API reads, and allowlisted report messages.
- `DiagnosticsForm`: asynchronous checks, opt-in node probe, and local export of the displayed report.
- `NodeCatalog` / `NodePreferences`: display-only search/sort and atomic per-profile favorite storage.
- `StartupConfiguration`: per-user settings and active-profile resolution.
- `MihomoConfigFactory`: safe process-scoped configurations for local upstreams.
- `ProfileManagerForm`: multi-profile selection and source onboarding.
- `ProviderConfigFactory`: HTTP subscriptions and file-backed URI/Base64 providers.
- `ManagerContext` automation: provider refresh, group delay ranking, current-node health checks, and failover after three consecutive failures.
- Split controller plane: traffic statistics and connection resets stay on the bundled Codex-only core, while node operations may target a user-selected local Mihomo-compatible API.
- Bundled core: an unmodified Mihomo executable controlled through its local API.

All private profiles, logs, runtime state, and mutable core data live under:

```text
%LOCALAPPDATA%\CodexProxyManager
```

The source directory contains only templates and immutable application assets.

## v0.6 isolation boundary

Normal sessions never enable a Windows system proxy or change global environment
variables. The manager passes `--proxy-server` to the installed Codex executable
and HTTP(S)/ALL_PROXY to its child environment. Existing routing profiles remain
unchanged. Other independently started apps retain their existing network path.
This is environment/launch scoping, not transparent interception of arbitrary
applications: a child ignoring proxy variables is not forcibly intercepted.

New state files identify `NetworkMode: process`. All restore paths skip registry
writes for these sessions. Only legacy sessions may restore a previously saved
system proxy, conditional on the current endpoint still belonging to this manager.
The emergency script follows the same distinction. Stopping the local core does
not remove proxy arguments from an already running Codex. v0.11.1 can resume the
core for matching Chromium/Electron launches without restarting the target, even
after reopening the manager. Switching to direct, changing privacy flags or
attaching to an ordinary launch still requires a restart. This is endpoint
resumption, not runtime proxy injection; arguments are evidence of requested
configuration, not proof of application behavior.

Regression tests exercise child environment inheritance, actual isolated Mihomo
startup/shutdown, unchanged Windows Internet Settings, crash cleanup, and stale
watchdog ownership. Full Codex/Store/game co-existence needs interactive validation.

## Application sessions (v0.7 onward)

v0.7 supports one selected application per session. Each application can bind a
profile or follow the manager's active profile. Missing app fields in v0.6 settings
normalize to the built-in Codex entry; profile data is retained. Settings writes
use a same-directory temporary file and atomic replacement.

For custom applications, only standard Codex process names inside the YAML rules
block are replaced in a per-user session copy. Nodes, provider content and original
profiles are preserved. The copy is removed after a normal stop. Codex continues
to use its original configuration. No system proxy fallback is available.

The selected executable must support Chromium proxy switches or environment
proxies. Unrelated helper executable names are not automatically routed, and this
does not provide transparent interception of games or UWP applications.

## v0.8 diagnostics boundary

The dashboard captures an input snapshot for diagnostics. The separate
`--diagnostics` entry point reads saved settings/state without launching a session,
acquiring the manager mutex, or persisting settings. Private input fields (paths,
controller secrets and group names) are never serialized into reports.

Default checks are local-only: required files, target process, session state,
Windows manual proxy/PAC settings, the mixed port, core authentication, node
selection, and target-process connection counts. A missing target connection is
inconclusive; the report does not equate it with failure. Registry inspection does
not cover TUN, VPN, or automatic proxy discovery. No check repairs settings,
switches nodes, starts applications, or changes the core lifecycle.

Controller requests accept only normalized loopback URLs, disable redirects and
HTTP proxy inheritance, and limit response size. API connection/read timeouts are
3.5 seconds (7 seconds for the optional probe); the local TCP check waits up to
600 ms. Cancellation is checked between operations. The node probe is off by
default and explicitly requests gstatic's generate_204 endpoint through the
selected node; its success does not establish application login availability.

Reports use fixed messages and numeric counts/delays, never raw exception text,
API bodies, connection destinations, node identities, credentials, or logs.
Export writes the completed on-screen snapshot as UTF-8 text to a user-selected
file. There is no upload or automatic clipboard copy. Isolated tests cover live
APIs, bad authentication, occupied stopped-session ports, default probe omission,
process filtering, privacy fixtures, and timeout handling.

## v0.9 node catalog boundary

The dashboard owns a copied node snapshot. Search (name/protocol), sorting and
favorite filtering operate on that copy only; ManagerContext retains its complete
node list for latency tests, automatic selection and failover. Row tags always
carry exact node names. Sorting preserves selection by identity; filtering a
selection out clears it so a hidden row cannot be switched accidentally. The
visible/total count and hidden-current warning make filtering explicit.

Favorites are stored separately in per-user `node-preferences.json`, keyed by
profile ID and exact, case-sensitive node name. Profile names may change without
losing favorites. Temporarily missing nodes retain their entries; renamed nodes
need to be favorited again. Profile changes clear the previous view and filters.
Writes reread the file, update one profile, then atomically replace the file.
Malformed data is not silently overwritten. The single manager owns normal
writes; concurrent manual editing is not supported. This file contains private
node names and is excluded from diagnostics and source control.

`--preview` has memory-only favorites and no proxy lifecycle. Tests exercise pure
filter/sort behavior, native ListView selection identity, filter reset, persistence,
profile isolation and corrupt-file preservation alongside network regressions.

## v0.10 onboarding and release boundary

AppInfo.Version is the sole numeric version source. The EXE filename is fixed;
build scripts parse the version for package metadata; assembly attributes and UI/report
titles use it directly. build-common.ps1 owns the production source-file list.

InstallationCheck reads required nonempty files, accepting cached per-user
geodata. It does not execute or authenticate the core. First-run and missing-file
paths show GettingStartedForm before creating user directories or opening profile
configuration. Closing the guide leaves settings untouched. New profile defaults
disable auto-start; existing saved preferences are preserved. --guide is read-only
and bypasses normal session startup and the manager mutex.

package.ps1 compiles into a unique temporary directory and copies only explicit
release inputs, excluding all runtime and user data. It includes a file-hash
manifest and writes a separate ZIP hash, refusing an existing destination.
test-package.ps1 builds an isolated fixture containing forbidden canary files and
verifies exact archive entries, hashes, version and overwrite refusal. These
checks do not prove that allowed source/doc contents are free of private data.
UnitOnly tests omit installed-Codex and core-dependent integration; the complete
test mode retains those checks. Earlier releases have passed hosted CI; a new
local revision is not validated there until its own run passes. The project's original source is GPL-3.0-only,
attributed to Rinko; third-party licenses remain separate. Public distribution
remains gated by RELEASE_CHECKLIST.md; this is not a signed, full-runtime v1.0 release.

## v0.10.1 acceptance fix

The profile editor's external-controller check previously used synchronous
WebClient calls on the UI thread. It now captures address/secret before awaiting
a worker task and reuses Diagnostics.ReadApi's loopback-only, no-proxy,
no-redirect transport with 2.5-second request/read timeouts. The trigger is
disabled while pending; completion avoids disposed controls. Results include
only a selector count or fixed error categories, not raw server text. Closing
the form does not abort an in-flight socket; it completes or times out in the
background. Since v0.11.2 each request also has an abort deadline for slow-drip bodies.

ControllerProbeTests use local fixture servers for authentication, invalid JSON
structure, redirects and slow responses. A hidden native form plus timer/message
pump verifies the handler returns promptly and that the UI continues processing
events until completion, without saving configuration. These are local tests,
not a substitute for clean-OS or real external-client acceptance.

## v0.11.2 recovery and persistence hardening

Session state is size-bounded and atomically replaced. New states identify the
session token and exact core executable/start time; the watchdog requires the
original token and never kills a core solely by PID/name. Unverifiable legacy
process identities are not killed by the watchdog. Scoped orphan cleanup checks
an exact `-f` argument, executable and WMI creation time (microsecond precision).
Recovery tooling also rechecks executable/creation time before stopping a process.
All recorded paths/state stay in private user data, never diagnostic exports.

Settings and favorites reject linked paths and oversized input. User config paths
must be relative, contained in the data root, without alternate streams or linked
parents. Duplicate/null profile identifiers fail with the original file retained.
Controller URLs accept IPv4/IPv6 loopback or localhost, not URL credentials,
non-root paths, query strings or fragments. These are local integrity checks,
not encryption or protection against an attacker running as the same user.

## Planned backend adapters

Since v0.11.3 lifecycle, node policy and polling have separate service boundaries,
and unit tests inject IControllerClient fixtures rather than reflecting into
ManagerContext's process fields. This prepares for adapters but does not implement
sing-box, multi-session operation or transparent interception. The core remains
Mihomo-specific, and the selected application still needs launch proxy support.

Each backend should eventually implement start, stop, health, traffic, node list,
latency test, and node switch operations. Planned adapters are:

1. Bundled Mihomo with HTTP subscriptions, URI/Base64 providers, or managed nodes.
2. Local HTTP/SOCKS5 upstream exposed by another proxy application.
3. External Mihomo-compatible REST API (implemented in v0.5 for loopback controllers).
4. sing-box after its API and configuration behavior are isolated in an adapter.

Protocol encryption and transport implementations do not belong in this project.
They remain the responsibility of the selected proxy core.
