# ProtocolLens — Static Source Review, Findings, and Study Handoff

**Review mode:** read-only inspection of files in `C:\Users\z004m50r\Github\ProtocolLens`. Nothing was built, run, or tested for this report. No commit SHA was supplied; the audited snapshot is the working tree as read. Branch `main` (from IDE context).

**Provenance disclosure.** An earlier AI-agent session in the same conversation *added* `ProtocolLens.Core/`, `ProtocolLens.Core.Tests/`, edited `ProtocolLens.slnx` and `ProtocolLens.Api.csproj`, and **deleted `ProtocolLens.Api/Services/PcapReader.cs`** intending to replace it; the replacement write was interrupted and never completed. The working tree is therefore in a **non-compiling intermediate state** (Finding M1). The agent-authored code was treated as untrusted, and shared assumptions between it and its tests were specifically looked for — several were found (Findings M2, M3, M4).

**Evidence categories used throughout**

| Category | Meaning |
|---|---|
| Supported by source | Grounded in inspected code |
| Source-demonstrable defect | Concrete defect shown by an identifiable code path and counterexample |
| Inferred rationale/behavior | Reasonable interpretation; assumptions stated |
| Previously reported result | Existing execution claim; not re-verified |
| Not assessed | Requires unavailable source or runtime evidence |

---

## 1. What exists today (plain language)

**Supported by source:**

- **Backend (`ProtocolLens.Api`)** — a minimal-API ASP.NET Core app with two endpoints: `GET /api/status` and `POST /api/pcap` (multipart `file`). The upload handler writes the file to a random temp path, calls a `PcapReader` to extract DHCP packets via SharpPcap/PacketDotNet, and returns them as an anonymous JSON object. **`PcapReader.cs` is currently absent**, so this project cannot compile as-is.
- **Core library (`ProtocolLens.Core`)** — pure, dependency-free logic: a normalized `DhcpObservation` record, a `ConversationReconstructor` (groups by transaction ID + identity evidence, with a 30 s attempt-split heuristic and an "unresolved" bucket), a `FlowClassifier` (8 flows + Unknown), a `Finding` record, and a `DhcpConversationAnalyzer` emitting 8 rule codes. **Nothing in the API calls this library yet.**
- **Core tests (`ProtocolLens.Core.Tests`)** — 17 xUnit test methods over hand-built observations (no capture bytes). Execution not assessed; two are statically predicted to fail (M3, M4).
- **Frontend (`protocol-lens-ui`)** — an Angular **21.2** (not 20) scaffold with a "Check API Status" button and a bare `<input type=file>` that POSTs to `http://localhost:5015/api/pcap` and `console.log`s the response. No results view, timeline, findings, filtering, export, or state handling exists.
- **README.md (619 lines)** — a detailed *product vision* (Quick/Engineer/Learn views, coverage counters, JSON export, option-overload handling, episode boundaries). Almost none of it is implemented. Read it as a specification, not a description.

**Main user journey in source:** pick a file → browser POSTs it → server extracts DHCP packets → raw list logged to the browser console.

**Feature status**

| Area | Status |
|---|---|
| PCAP ingestion via SharpPcap | Partial; file deleted in working tree; last-known version extracted only xid, msg type, chaddr, IPs/ports, timestamp |
| DHCP option decoding (50/54/61, overload, concatenation) | Absent |
| Conversation reconstruction | Implemented in Core; not wired to API; correctness defect (M2) |
| Flow classification | Implemented in Core (conservative) |
| Findings | 8 rule codes in Core; one wording/test inconsistency (M4); one rule unreachable for a tested flow (M3) |
| Coverage/diagnostics/limits/cancellation | Absent |
| Angular results UI, export, state handling | Absent |
| Integration/browser tests, fixtures, CI, docs beyond README | Absent |

**Should source issues be fixed before deep study?** Yes — M1 (build break) and M2 (identity split defect) first; M3/M4 are test/wording fixes. The Core algorithm is small enough to study now, provided M2 is treated as a known bug rather than intended behavior.

**Technology table (from manifests; installed versions not inspected)**

| Layer | Configured | Why it exists |
|---|---|---|
| Backend | `net10.0`, `Microsoft.NET.Sdk.Web`, `Microsoft.AspNetCore.OpenApi 10.0.10` | Minimal API host; OpenAPI mapped only in Development |
| Capture reading | `SharpPcap 6.3.1` | `CaptureFileReaderDevice` reads pcap/pcapng; **needs native libpcap/Npcap at runtime** (not verified) |
| Packet decoding | `PacketDotNet 1.4.8` | `Packet.ParsePacket` → `IPv4Packet`/`UdpPacket`/`DhcpV4Packet` |
| Core | plain `net10.0` class library, no packages | Deterministic logic isolated from web/native |
| Backend tests | `xunit 2.9.2`, `Microsoft.NET.Test.Sdk 17.12.0` | Unit tests over Core |
| Frontend | `@angular/* ^21.2.0`, `rxjs ~7.8.0`, `typescript ~5.9.2`, `vitest ^4.0.8`, `jsdom ^28` | Angular 21 standalone app; vitest default runner |

> **Inconsistency:** stated target Angular 20; `package.json` configures Angular 21.2. `package-lock.json` present, not inspected in detail.

---

## 2. Repository structure and responsibilities

```
ProtocolLens.slnx                        (Api, Core, Core.Tests)
README.md                                (vision document, 619 lines)
.gitignore                               (bin/obj/node_modules/*.pcap/*.pcapng)
ProtocolLens.Api/
  Program.cs                             startup, CORS, 2 endpoints, temp-file lifecycle
  Models/DhcpPacketInfo.cs               legacy DTO (mutable class, 8 props)
  Services/PcapReader.cs                 *** ABSENT in working tree ***
  ProtocolLens.Api.csproj                refs Core + SharpPcap + PacketDotNet
  appsettings*.json, Properties/launchSettings.json (http://localhost:5015)
ProtocolLens.Core/
  Model/DhcpObservation.cs               record: evidence unit + IdentityEvidence derivation
  Model/Conversation.cs                  Conversation, UnresolvedObservation
  Reconstruction/ConversationReconstructor.cs
  Analysis/DhcpFlow.cs, FlowClassifier.cs, Finding.cs, DhcpConversationAnalyzer.cs
ProtocolLens.Core.Tests/
  ObsBuilder.cs, ReconstructionTests.cs (7), AnalyzerTests.cs (10 incl. 2-case Theory)
protocol-lens-ui/src/app/
  app.ts/.html/.config.ts/.routes.ts     root component, status button, provideHttpClient
  pcap-upload.service.ts                 POST http://localhost:5015/api/pcap
  status.service.ts                      GET http://localhost:5000/api/status  (port mismatch)
  pcap-upload.component/*                file input → service → console.log
  *.spec.ts                              "should create" scaffolds
```

| File / module | Main symbols | Responsibility | Inputs → Outputs | Dependencies / callers |
|---|---|---|---|---|
| `ProtocolLens.Api/Program.cs` | top-level statements; `MapGet("/api/status")`, `MapPost("/api/pcap")` | Host startup, CORS, endpoints, temp-file lifecycle | `IFormFile` → anonymous JSON | `PcapReader` (new'ed inline) |
| `ProtocolLens.Api/Models/DhcpPacketInfo.cs` | `DhcpPacketInfo` | Legacy per-packet DTO | — | Last-known `PcapReader`; superseded by `DhcpObservation` |
| `ProtocolLens.Api/Services/PcapReader.cs` | `PcapReader.ExtractDhcpPacketsFromPcapFile(string)` (last known) | SharpPcap/PacketDotNet integration | file path → `List<DhcpPacketInfo>` | **Absent** |
| `Core/Model/DhcpObservation.cs` | `DhcpMessageType`, `DhcpObservation`, `IsRelayed`, `IdentityEvidence` | Immutable evidence unit | — | Reconstructor, Analyzer, tests |
| `Core/Model/Conversation.cs` | `Conversation`, `UnresolvedObservation` | Grouping result | — | Reconstructor output; Analyzer input |
| `Core/Reconstruction/ConversationReconstructor.cs` | `Reconstruct`, `AttemptGapThreshold`, `BuildConversation`, `ReconstructionResult` | Grouping algorithm | `IEnumerable<DhcpObservation>` → `ReconstructionResult` | Tests only (not API) |
| `Core/Analysis/DhcpFlow.cs` | `DhcpFlow` enum | Inferred flow labels | — | Classifier, Analyzer |
| `Core/Analysis/FlowClassifier.cs` | `Classify(Conversation)` | Flow inference | `Conversation` → `DhcpFlow` | Analyzer |
| `Core/Analysis/Finding.cs` | `Finding`, `FindingSeverity`, `FindingCategory` | Structured finding | — | Analyzer output |
| `Core/Analysis/DhcpConversationAnalyzer.cs` | `Analyze`, `AnalyzeInitialAllocation`, `AnalyzeRequestOnlyFlow`, `AnalyzeInform` | Flow-conditional rules | `Conversation` → `IReadOnlyList<Finding>` | Tests only |
| `Core.Tests/ObsBuilder.cs` | `ObsBuilder.Make(...)` | Synthetic observation factory (independent of production decoding) | params → `DhcpObservation` | Both test classes |
| `Core.Tests/ReconstructionTests.cs` | A01, A05, A06, A14, A16, A17, A24 | Grouping behaviors | — | Reconstructor |
| `Core.Tests/AnalyzerTests.cs` | A01, A02, A03, A04, A08, A09, A11, A12 (Theory), UnknownFlow | Rule behaviors | — | Reconstructor + Analyzer |
| `protocol-lens-ui/src/app/app.ts` + `.html` | `App`, `collectResponse()`, `statusMessage` | Root shell, status button | — | `StatusService` |
| `app.config.ts` | `appConfig` | Providers: router, HttpClient, global error listeners | — | `main.ts` |
| `pcap-upload.service.ts` | `PcapUploadService.onUploadPcapFile(FormData)` | HTTP POST | `FormData` → `Observable<Object>` | Component |
| `status.service.ts` | `StatusService.getStatus()` | HTTP GET (port 5000) | → `Observable<StatusResponse>` | `App` |
| `pcap-upload.component.ts/.html` | `PcapUploadComponent.onPcapFileSelected(Event)` | File input handler | DOM event → subscription | `PcapUploadService` |

**Entry points / DI.** `Program.cs` registers only `AddOpenApi()` and CORS policy `AngularPolicy` (origin `http://localhost:4200`). `PcapReader` is not registered; it is `new`ed inside the endpoint lambda. Core types are static or plain classes; no DI. Angular: both services `providedIn: 'root'`; `provideHttpClient()` in `app.config.ts`.

**Shared mutable state.** None in Core (records + local lists). API: per-request temp file. `DhcpPacketInfo` mutable but request-scoped.

**Coupling notes.** `Program.cs` hard-wires `PcapReader` and an anonymous response shape — no DTO layer. Angular hardcodes absolute URLs on two different ports. `ConversationReconstructor.BuildConversation` stores the same `List<DhcpObservation>` instance later mutated via `identityGroups[0].Add(obs)`; works only because the conversation is rebuilt afterwards (S3).

```mermaid
flowchart LR
  UI[Angular upload] --> EP[POST /api/pcap]
  EP --> RD[PcapReader absent]
  RD -. not wired .-> RC[Core Reconstruct]
  RC --> AN[Core Analyze]
```

---

## 3. End-to-end trace (as written, including the break)

Illustrative input (not executed): a classic pcap with 4 DHCP frames at original frame numbers 12/15/18/21 (DISCOVER/OFFER/REQUEST/ACK, xid `0x39a2f17c`, chaddr `02:00:00:00:00:01`, no option 61) plus unrelated frames.

| # | Step | File / symbol | In → Out | Notes |
|---|---|---|---|---|
| 1 | User picks file | `pcap-upload.component.html` `(change)="onPcapFileSelected($event)"` | DOM `Event` → `File` | No drag/drop, no size/type guidance, no disabled state |
| 2 | Component builds form | `PcapUploadComponent.onPcapFileSelected` | `File` → `FormData{file}` | Subscribes immediately; `next` → `console.log`, `error` → `console.error`. No state, no stale-response guard, no unsubscribe (HttpClient completes, so leak-free but uncancellable) |
| 3 | HTTP | `PcapUploadService.onUploadPcapFile` | `FormData` → `Observable<Object>` | Untyped `post`; URL hardcoded |
| 4 | Endpoint | `Program.cs` `app.MapPost("/api/pcap", async (IFormFile file) => …)` | multipart → `IFormFile` | `.DisableAntiforgery()`; `UseHttpsRedirection()` enabled but `http` profile has no HTTPS port (framework behavior; not assessed). Null/empty → 400 anonymous `{message}` (not ProblemDetails) |
| 5 | Temp file | `Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())`; `await file.CopyToAsync(tempStream)` | stream → disk | Client filename never used as a path. `finally` deletes the file. No size limit beyond Kestrel defaults; no `CancellationToken` |
| 6 | Reader | `new PcapReader().ExtractDhcpPacketsFromPcapFile(tmpFilePath)` | path → `List<DhcpPacketInfo>` | **File absent → compile error (M1).** Last-known content: `CaptureFileReaderDevice.Open()`; loop `GetNextPacket`; `Packet.ParsePacket(LinkLayerType, Data)`; match `IPv4Packet`→`UdpPacket`→ports 67/68→`DhcpV4Packet`; copies xid, `MessageType.ToString()`, IPs, ports, chaddr, timestamp. **Frame counter incremented but never stored** — original frame numbers lost. Synchronous native loop on the request thread |
| 7 | Reconstruction / analysis | — | — | **Not invoked.** Core referenced by csproj but unused |
| 8 | Response | `Results.Ok(new { fileName, fileSize, dhcpPackets })` | anonymous → JSON | Echoes client `fileName` (safe as JSON). Any exception → `Results.Problem(detail: ex.Message, 500)` — leaks internal messages; a non-PCAP file becomes 500 not 400 |
| 9 | Angular state | none | — | Console only. No timeline/findings/export |

**Concepts used:** *minimal API lambda* = endpoint without controller; *`IFormFile` binding* = ASP.NET reads multipart part named `file`; *`await CopyToAsync`* = async I/O, but the following SharpPcap loop is synchronous native/CPU work; *`Observable.subscribe`* = HTTP call starts on subscribe and completes after one emission.

### Shorter traces (Core only; the API never reaches these today)

**REQUEST with no ACK/NAK (A03):** `Reconstruct([D1,O2,R3])` → one conversation (all `mac:…`) → `Classify` sees `Discover` → `InitialAllocation` → `AnalyzeInitialAllocation`: offers=1 (no MISSING-OFFER); request present, ack=nak=null → `DHCP-MISSING-ACK-OR-NAK`, `ObservedFrames=[3]`, `MissingObservation="No ACK or NAK observed … after frame 3"`, explanation "does not by itself establish that the server did not respond". Evidence semantics correct.

**Two clients, same xid (A05):** `GroupBy(TransactionId)` → one group; `withIdentity` split by exact `IdentityEvidence` string → two groups → two conversations with ids `"{xid:x8}-mac:aa…-1"` and `"…-mac:bb…-2"`. Correct **only** if both clients present the same *kind* of identity (see M2).

**Invalid upload:** empty → 400; anything throwing inside SharpPcap → generic catch → 500 with library message. No distinction between corrupt container, unsupported link type, truncated record; no coverage report.

**Ambiguous correlation (A16):** observation with `IdentityEvidence == null` and ≥2 identity groups for that xid → `UnresolvedObservation{Reason:"…multiple candidate conversations…"}`; 0 groups → also unresolved. Never force-assigned. Good principle — but M2 makes `null` arise spuriously.

---

## 4. Models and contracts

| Model | Key fields | Observed / derived | Notes |
|---|---|---|---|
| `DhcpObservation` (record; `required` frame/time/type/xid/chaddr) | `FrameNumber:long`, `Timestamp:DateTimeOffset`, `MessageType`, `TransactionId:uint`, `ClientHardwareAddress:string`, nullable `ClientIdentifierHex`, `ServerIdentifier`, `ClientIpAddress`, `YourIpAddress`, `RelayAgentAddress`, `RequestedIpAddress`, IPs/ports, `Diagnostics` | All observed except derived `IsRelayed` (giaddr non-empty and ≠ `0.0.0.0`) and `IdentityEvidence` | Timestamp precision = producer's (µs via `AddTicks(µs*10)` in last-known reader). No invariant enforcement beyond `required`. Address fields are unvalidated strings |
| `Conversation` | `ConversationId` = `$"{xid:x8}-{identity ?? "no-identity"}-{firstFrame}"`, `TransactionId`, `IdentityEvidence?`, `Observations` (frame-ordered), `GroupingRationale` | derived | Deterministic for same input. Identity string is in the id, so fixing M2 changes ids |
| `UnresolvedObservation` | `Observation`, `Reason` | derived | Free-text reasons |
| `DhcpFlow` enum | Unknown, InitialAllocation, InitReboot, Renewing, Rebinding, Inform, Release, Decline | **inferred** | Enum doc says inference |
| `Finding` | `RuleCode`, `Category`{FlowProgress,FlowOutcome,Correlation,CaptureLimitation}, `Severity`{Info,Notice,Warning}, `ConversationId`, `Flow`, `Expected`, `ObservedFrames`, `MissingObservation?`, `Explanation`, `Hypotheses`, `NextChecks`, `NotEvaluatedReason?` | mixed, explicitly separated | Severity and certainty separate — good. `Correlation`/`CaptureLimitation` never emitted |
| `DhcpPacketInfo` (Api legacy) | mutable, non-nullable strings without initializers | observed | Nullable warnings expected; superseded |
| API response | anonymous `{fileName, fileSize, dhcpPackets}` | — | No schema version, coverage, conversations, findings, limits. No TS type |
| Errors | 400 `{message}`; 500 `ProblemDetails{detail: ex.Message}` | — | No stable error codes |

**Does the contract distinguish HTTP success / completeness / outcome / severity / certainty?** In Core's `Finding`, yes. In the actual wire contract, **no** — none of it is exposed.

Illustrative `Finding` JSON (derived from contract, not executed):

```json
{ "ruleCode":"DHCP-MISSING-ACK-OR-NAK","category":"FlowOutcome","severity":"Notice",
  "conversationId":"39a2f17c-mac:02:00:00:00:00:01-12","flow":"InitialAllocation",
  "expected":"RFC 2131 4.3.2: a server would typically answer REQUEST with ACK or NAK.",
  "observedFrames":[18],
  "missingObservation":"No ACK or NAK observed for transaction 0x39a2f17c within the analyzed capture after frame 18.",
  "explanation":"… does not by itself establish that the server did not respond …",
  "hypotheses":[],"nextChecks":["Verify the capture extends sufficiently past this frame.","…"],
  "notEvaluatedReason":null }
```

---

## 5. Capture parsing and DHCP extraction

`PcapReader.cs` is absent; this describes the **last known content** seen earlier in the conversation.

- **Library vs app:** SharpPcap owns container parsing (`CaptureFileReaderDevice`, `GetNextPacket`, `GetPacket()` → `RawCapture{LinkLayerType, Data, Timeval}`). PacketDotNet owns L2–L7 (`Packet.ParsePacket`, `PayloadPacket` chain, `DhcpV4Packet.TransactionId/MessageType/ClientHardwareAddress`). App code: port 67/68 filter, field copying, `MessageType.ToString()`.
- **Not addressed in code:** link-type checks, 802.1Q (PacketDotNet may descend a VLAN tag — declared capability, not verified), PCAPNG variants, multi-interface, timestamp resolution beyond µs, IPv4 fragments, truncated/malformed packets (any decoder exception aborts the request with 500), unrelated-traffic counting, captured vs original length, per-packet diagnostics.
- **Options:** none decoded. ciaddr/yiaddr/giaddr not copied. Therefore `IsRelayed`, the cid branch of `IdentityEvidence`, INIT-REBOOT/RENEW/REBIND classification, and server-id/requested-ip logic can never be exercised from a real file today.
- **Frame numbers:** counted but not emitted; Core requires them. Provenance lost at the boundary.
- **Native dependency:** SharpPcap file reader uses libpcap/Npcap (wpcap.dll on Windows). Not verified; absence would surface as exception → 500.
- **Downstream qualification of parse gaps:** none; no coverage object exists.

---

## 6. Reconstruction and interpretation

### 6.1 Identity derivation (`DhcpObservation.IdentityEvidence`)

```
if ClientIdentifierHex non-empty                                          → "cid:" + hex
else if !IsRelayed && chaddr non-empty && chaddr != "00:00:00:00:00:00"   → "mac:" + chaddr
else                                                                      → null
```

Three identities exist conceptually: **client** (`IdentityEvidence`), **transaction** (`TransactionId`), **attempt** (30 s split). They are distinct in code — good — but client identity is a *single string compared by equality*, the root of M2.

### 6.2 Grouping algorithm (`ConversationReconstructor.Reconstruct`) — faithful pseudocode

```
ordered  = observations sorted by FrameNumber
for each txGroup in ordered.GroupBy(TransactionId):          // first-appearance order
	withId    = txGroup where IdentityEvidence != null
	withoutId = txGroup where IdentityEvidence == null
	identityGroups = []                                       // List<List<Obs>>
	for obs in withId:                                        // frame order
		cand = identityGroups
				 .Where(g => g[0].IdentityEvidence == obs.IdentityEvidence)
				 .OrderByDescending(g => g.Last.FrameNumber)
				 .FirstOrDefault(g => obs.Timestamp - g.Last.Timestamp <= 30s)
		if cand: cand.Add(obs) else identityGroups.Add([obs])
	for g in identityGroups: conversations.Add(Build(txid, g, rationale1))
	for obs in withoutId:
		if identityGroups.Count == 1: identityGroups[0].Add(obs)   // mutates list already inside a Conversation
		else: unresolved.Add(obs, reason(count==0 ? "no other obs" : "multiple candidates"))
	if identityGroups.Count == 1 && withoutId.Count > 0:
		replace that conversation with Build(txid, sorted(identityGroups[0]), rationale2)
conversations sorted by first FrameNumber
```

**Behavior walk-through**

| Scenario | What the code does | Verdict |
|---|---|---|
| Different clients, same xid | Split by identity string | Correct if both carry the same identity *kind*; else M2 |
| Same client reuses xid later | New group when gap > 30 s since latest same-identity group's last obs | Documented heuristic; a client retrying every 60 s is split per retry |
| Retransmission / duplicate | Kept in same conversation; no dedupe, no labeling | Evidence retained; no unsupported certainty; A07 not addressed |
| Multiple offers | Same group; `DHCP-MULTIPLE-OFFERS` (Info) | Correct |
| Server reply lacking option 61 while client sent it | Client → `cid:…`, server → `mac:…` → **two conversations** | **Defect M2** |
| Relay traffic, client without option 61 | `IsRelayed` suppresses chaddr fallback → `null` → unresolved (or attached only if exactly one group) | **Defect M2b**: chaddr is the client's hardware address even through relays; `giaddr` is the relay field |
| Late reply after 30 s | Starts a new single-observation Unknown conversation; original gets spurious MISSING-ACK | Heuristic artifact, not surfaced as limitation |
| Timestamp regression | negative diff ≤ 30 s → joins | Tolerant, undocumented |
| Conflicting identifiers (same cid, different chaddr) | Only cid compared | Not detected |

**Complexity.** *n* = observations, *T* = distinct xids, *n_t* = observations for xid *t*, *g_t* = identity groups for xid *t*. Sort O(n log n); GroupBy O(n); per observation a `Where` over g_t groups plus `OrderByDescending` O(g_t log g_t) → Σ_t O(n_t·g_t log g_t). Worst case (every observation its own group) O(n² log n) per xid. Space O(n) plus per-group list copies and per-observation `OrderByDescending` allocations. Fine for the intended 10 000-observation bound; not O(n).

### 6.3 Flow decision table (`FlowClassifier.Classify`, top-down)

| Condition | Flow |
|---|---|
| any RELEASE | Release |
| any DECLINE | Decline |
| any INFORM | Inform |
| any DISCOVER | InitialAllocation |
| first REQUEST has ciaddr ≠ 0 and dest ≠ broadcast | Renewing |
| first REQUEST has ciaddr ≠ 0 and dest = broadcast/"" | Rebinding |
| first REQUEST has option 50 | InitReboot |
| otherwise (REQUEST w/o ciaddr & option 50, OFFER-only, ACK-only, NAK-only) | Unknown |

Does **not** force DORA; partial captures become Unknown or a request-only flow. Rebinding vs renewing by destination is an approximation of RFC 2131 §4.3.6 (which also uses server-identifier presence). A REQUEST without ciaddr and option 50 (non-conformant for SELECTING) is classified Unknown rather than flagged — acceptable conservatism.

### 6.4 Rule table (`DhcpConversationAnalyzer`)

| Rule code | Flow(s) | Trigger | Severity/Category | Uncertainty handling | Test |
|---|---|---|---|---|---|
| DHCP-MISSING-OFFER | InitialAllocation | DISCOVER present, 0 OFFERs | Notice / FlowOutcome | `MissingObservation` scoped to analyzed capture; hypotheses labelled | A02 |
| DHCP-MULTIPLE-OFFERS | InitialAllocation | >1 OFFER | Info / FlowProgress | states "normal" | A08 |
| DHCP-MISSING-ACK-OR-NAK | InitialAllocation, InitReboot, Renewing, Rebinding, Inform | REQUEST/INFORM, no ACK/NAK | Notice / FlowOutcome | "does not by itself establish…" (InitialAllocation path only; other paths weaker) | A03 |
| DHCP-NAK-OBSERVED | **InitialAllocation only** | NAK present | Info | "does not establish why" | A04 (test uses InitReboot → **M3**) |
| DHCP-ACK-OBSERVED | InitialAllocation | ACK, no NAK | Info | denies receipt/connectivity proof | A01 (**M4** wording vs assertion) |
| DHCP-FLOW-COMPLETED | InitReboot/Renewing/Rebinding | ACK or NAK | Info | **collapses NAK into "completed"** | A09 |
| DHCP-INFORM-ACK-OBSERVED | Inform | ACK | Info | "not a newly allocated lease" | A11 |
| DHCP-NO-RESPONSE-EXPECTED | Release, Decline | always | Info | n/a | A12 |
| DHCP-FLOW-UNKNOWN | Unknown | always | Info + `NotEvaluatedReason` | explicit | UnknownFlow test |

**Evidence checks:** absence→root cause **no**; forced DORA **no**; multiple offers/NAK malformed **no**; INFORM ACK = lease **no**; ACK after RELEASE/DECLINE **no**; ACK = connectivity **no**; parse failures ignored **yes — nothing carries them**; ambiguous packet force-assigned **no** (but M2 causes spurious splits). 30 s threshold is a reconstruction heuristic, not a protocol claim in findings — but its side effects are not surfaced. ACK and NAK both present → only NAK reported (acceptable). RFC strings (`4.3.1`, `4.3.2`, `4.4.3`) match RFC 2131 topics; errata not re-verified.

---

## 7. Frontend

`PcapUploadComponent` has one handler and no template state; `App` has a status button bound to `statusMessage`. No loading/empty/partial/error states, no duplicate-submit guard, no stale-response handling, no typed models, no export, no accessibility work beyond native controls, no interpolation of packet text (no XSS surface yet). `StatusService` targets port **5000** while the API profile and upload service use **5015** — the status button cannot work against the configured profile (source-demonstrable). `pcap-upload.component.spec.ts` imports a component whose service requires `HttpClient`, but TestBed provides none → predicted `NullInjectorError` (static inference; not assessed).

---

## 8. Tests as code

| Behavior | Test & assertion | Defect it would catch | Weakness |
|---|---|---|---|
| Single conversation, frame order | Reconstruction A01: count==1, frames [1,2,3,4] | lost/reordered observations | same-kind identity only |
| Two clients same xid | A05: 2 convs, 2 obs each, different identity | grouping by xid alone | both mac-based (masks M2) |
| Same client reuses xid | A06: 10-min gap → 2 convs | no attempt split | 29 s/31 s boundary untested |
| Relay uses cid | A14: `StartsWith("cid:")` | mac used despite relay | relay without option 61 untested (M2b) |
| No-identity with 2 groups → unresolved | A16 | forced assignment | **encodes M2b** (shared assumption) |
| Determinism | A24 | random/time-based ids | one shape only |
| ACK wording | Analyzer A01: `DoesNotContain("connect")` + `Contains("does not establish")` | connectivity claims | **contradicts text "achieved connectivity" — predicted FAIL (M4)** |
| NAK valid | A04: `Single(... "DHCP-NAK-OBSERVED")` | NAK as failure | **input classifies InitReboot → rule not emitted — predicted FAIL (M3)** |
| Missing offer/ack wording | A02, A03 | missing qualification | string-fragment assertions brittle |
| RELEASE/DECLINE | A12 Theory | fabricated ACK expectation | good counterexample |
| Unknown flow | explicit NotEvaluated | accusation on no evidence | good |

No test uses capture bytes; `ObsBuilder` is independent of production decoding (good). No parser, HTTP, browser, fixture, performance, or mutation evidence exists. "Test exists; execution not assessed" applies to all 17.

**Recommended additional scenarios (not implemented):** DISCOVER with cid + OFFER without cid → one conversation; relayed DISCOVER without cid → grouped by chaddr; 29 s vs 31 s boundary; late ACK at 45 s not creating a second Unknown conversation (or attaching a limitation); REQUEST+NAK for every request-only flow; same cid different chaddr → conflict; timestamp regression policy; byte-level fixture through `PcapReader` asserting original frame numbers.

---

## 9. Prioritized findings

### Must Fix

**M1 — Working tree does not compile.** `ProtocolLens.Api/Services/PcapReader.cs` is absent while `Program.cs` calls `new PcapReader().ExtractDhcpPacketsFromPcapFile(...)`. Proven from source. *Fix:* restore the reader emitting `DhcpObservation` with frame numbers; wire `ConversationReconstructor`/`DhcpConversationAnalyzer` into the endpoint. *Test:* integration test posting a tiny synthetic pcap, asserting conversations ≥ 1.

**M2 — Identity compared as exact string splits one exchange into two.** `IdentityEvidence` yields `cid:` when option 61 is present, `mac:` otherwise; reconstructor groups by equality. *Counterexample:* DISCOVER{cid=01aa…, chaddr=aa…} + OFFER{no cid, chaddr=aa…}, same xid → two conversations → false `DHCP-MISSING-OFFER` plus an offer-only Unknown conversation. Proven from source. Evidence corruption (RFC 6842 makes echoing optional; RFC 2131 §4.3.1 identifies clients by client-identifier *or* chaddr). *Fix:* identity value object `{cid?, chaddr}` with a compatibility predicate (equal cid when both present; else equal chaddr; conflicting non-null cids → incompatible); group by compatibility. *Tests:* the mix above; same cid different chaddr → conflict.
**M2b (same root):** `IsRelayed` suppresses chaddr fallback. `chaddr` remains the client's address through relays; only the Ethernet source is the relay's, which this field does not hold. Any relayed client without option 61 becomes wholly unresolved. *Fix:* drop the `!IsRelayed` guard. Test A16 must be rewritten; it currently encodes the defect.

**M3 — `DHCP-NAK-OBSERVED` unreachable for request-only flows; test A04 contradicts implementation.** `AnalyzeRequestOnlyFlow` emits `DHCP-FLOW-COMPLETED` for a NAK, labeling rejection as completion. Proven from source. *Fix:* emit `DHCP-NAK-OBSERVED`/`DHCP-ACK-OBSERVED` there; remove `DHCP-FLOW-COMPLETED`. *Test:* A04 as written plus Renewing+NAK.

**M4 — ACK wording vs assertion.** `AnalyzerTests.A01` asserts `DoesNotContain("connect")` while the text says "achieved connectivity". The text is the better artifact. *Fix:* assert against affirmative claims (e.g. `DoesNotContain("client received")`). Proven from source.

**M5 — README materially overstates the implementation.** Unsupported completion claims. *Fix:* add an "Implementation status" section at the top; move the rest under "Target design".

### Should Improve

- **S1** `Program.cs`: generic `catch` → 500 with `ex.Message`; no size/time/concurrency limits; no `CancellationToken`; synchronous native loop on request thread. Map reader failures to 400/415 ProblemDetails with stable codes; add centralized limits.
- **S2** Angular: port mismatch (5000 vs 5015); untyped responses; no states; spec lacks HttpClient providers. Move base URL to a dev proxy/environment file.
- **S3** Reconstructor mutates a list already captured by a `Conversation`; build conversations only after attaching no-identity observations. Document timestamp-regression policy.
- **S4** Late-response artifact (>30 s) creates a new Unknown conversation while the original gets MISSING-ACK; attach a limitation note at minimum.
- **S5** `DhcpPacketInfo` is dead once the reader returns `DhcpObservation`; remove.
- **S6** `Finding.Category` values `Correlation`/`CaptureLimitation` never emitted; emit for unresolved observations or remove.

### Fine For Now
Static analyzer classes instead of DI; string address fields; 30 s heuristic as a documented constant; no persistence; CORS limited to `localhost:4200`; temp-file strategy.

**Proposed order:** M1 → M2/M2b (changes ids; do before snapshot tests) → M3/M4 → S1 → S2 → M5 docs → S3–S6. Keep as-is: `DhcpObservation`/`Finding` record shapes, the `Unresolved` bucket, the flow-conditional analyzer structure.

### A01–A26 mapping (source status only; no runtime labels)

| Status | Criteria |
|---|---|
| Implemented in source (Core, unit-tested, unexecuted) | A01, A02, A03, A05, A06, A08, A09 (partial: ACK-only → Unknown), A11, A12, A16 (present but partly wrong per M2b), A24 |
| Partial / inconsistent | A04 (M3), A14 (cid path only), A10 (classification only), A13 (no compatibility logic → M2), A17 (frame numbers lost at reader boundary) |
| Absent | A07, A15, A18, A19, A20, A21, A22, A23 (temp-path safe; rest absent), A25, A26 |

---

## 10. Decisions and tradeoffs

| Decision | Rationale (documented vs inferred) | Tradeoff / when to revisit |
|---|---|---|
| Separate `ProtocolLens.Core` library | Documented in code comments: deterministic, testable without web/native | Fine; revisit only if Core grows protocol-specific plugins |
| Identity as a derived string | Inferred: simplicity for equality grouping | Causes M2; replace with compatibility predicate |
| 30 s `AttemptGapThreshold` | Documented as heuristic in XML doc | Arbitrary value; make configurable and surface in findings |
| Static analyzer/classifier | Inferred: no state, no DI needed | Fine for now |
| Minimal API with inline `new PcapReader()` | Inferred: scaffold simplicity | Hinders testing/limits; register and inject when adding limits |
| Temp file via `GetRandomFileName` | Inferred: SharpPcap needs a path | Correct choice; keep |
| Angular 21 instead of 20 | Not documented (likely CLI default) | Decide explicitly; document support status |

---

## 11. Study handoff

**Study sequence (paper exercises; no runtime needed)**

| Step | Files / symbols | Concept | Exercise | Check question |
|---|---|---|---|---|
| 1 Product & evidence limits | `Analysis/Finding.cs`; README "Evidence model" | Observed vs missing vs inference | Label each `Finding` field by evidence class | Why is `Severity` not a certainty? |
| 2 Request/data flow | `Program.cs` `MapPost`; `pcap-upload.service.ts`; `pcap-upload.component.ts` | Minimal API, IFormFile, Observable subscribe | List every place a failure becomes an HTTP status | What status does a JPEG upload produce today and why is that wrong? |
| 3 Capture layers & fields | `Model/DhcpObservation.cs`; Kurose & Ross "DHCP" (network layer) and link-layer addressing | Encapsulation; chaddr vs Ethernet source via relay | Draw Ethernet→IPv4→UDP→BOOTP for a relayed DISCOVER; mark the relay's address | Which field would you use for client identity through a relay? |
| 4 Identity matching | `ConversationReconstructor.Reconstruct` | Grouping, candidate selection | Hand-trace A05, then the M2 counterexample | Which line decides the split? |
| 5 Flow & rules | `FlowClassifier.Classify`; `DhcpConversationAnalyzer.AnalyzeRequestOnlyFlow` | Flow-conditional expectations | Trace REQUEST(ciaddr, unicast)+NAK; name the rule | Is "completed" an honest label? |
| 6 Tests & counterexamples | `AnalyzerTests.A01`; `ReconstructionTests.A16` | Shared assumptions | Explain why each is predicted to fail or encodes a defect | What would a correct A16 input look like? |
| 7 Lifetime, complexity, tradeoffs | `Program.cs` try/finally; reconstructor loop | Disposal, cancellation, Big-O | Derive worst-case candidate scan | What makes the native loop uncancellable? |

**Functions to be able to rewrite from a spec:** `IdentityEvidence` (as a compatibility predicate), `Reconstruct`'s inner grouping loop, `FlowClassifier.Classify`, `AnalyzeInitialAllocation`.

**Ownership exercises (no solutions provided):**
1. Rebuild the grouping function from this spec: "same xid, compatible identity, ≤ gap → same attempt; incompatible → separate; insufficient identity with multiple candidates → unresolved."
2. Diagnose: a synthetic DISCOVER(cid)/OFFER(no cid) yields MISSING-OFFER. Find the line and propose the minimal change.
3. Add a scoped rule `DHCP-NAK-OBSERVED` for request-only flows with negative tests for Release/Decline/Inform.

**Interview questions (12)**

| # | Question | Answer outline (from code) | Follow-up | Misleading answer |
|---|---|---|---|---|
| 1 | Why can't xid alone group DHCP messages? | 32-bit client-chosen; collisions and reuse; code uses xid + identity + 30 s gap | What breaks when identity is a string? | "xid is unique per lease" |
| 2 | How do you identify a client through a relay? | chaddr/option 61 persist; giaddr marks relay; Ethernet source is relay's | Where does this code get it wrong? (M2b) | "use the source MAC" |
| 3 | What does an observed ACK prove? | Capture point saw it; not receipt/config/connectivity (`DHCP-ACK-OBSERVED` text) | How would you word a UI badge? | "the client got an IP" |
| 4 | Is a missing OFFER a server failure? | Missing observation within a window; hypotheses separate | How would coverage data change wording? | "server is down" |
| 5 | Which flows don't start with DISCOVER? | INIT-REBOOT, RENEW, REBIND, INFORM, RELEASE, DECLINE; decision table | How does the classifier distinguish RENEW vs REBIND? | "all valid exchanges are DORA" |
| 6 | Complexity of `Reconstruct`? | O(n log n) sort + Σ O(n_t·g_t log g_t) | What input maximizes g_t? | "O(n) because GroupBy" |
| 7 | Why C# `record` with `required`/`init`? | Immutable evidence; value equality; compile-time required | Why is `Observations` still mutable underneath (S3)? | "records are just classes" |
| 8 | What does `await CopyToAsync` buy, and what doesn't it? | Async I/O; SharpPcap loop afterwards blocks | Where would a `CancellationToken` not help? | "await makes everything non-blocking" |
| 9 | Why map reader exceptions to 400/415 not 500? | Client vs server fault; stable codes; no message leakage | What ProblemDetails fields would you populate? | "500 is fine, it's an error" |
| 10 | Two files selected quickly in Angular — what happens? | Two subscriptions; last response wins arbitrarily; fix with `switchMap`/request token | How to cancel the in-flight HTTP? | "HttpClient cancels automatically" |
| 11 | Why is `providedIn:'root'` fine here? | Stateless HTTP wrappers | When would per-component state be needed? | "always use root" |
| 12 | Which test passes with a wrong implementation? | A16 encodes M2b; A05 masks M2 | Design the separating counterexample | "all green means correct" |

**Competencies to demonstrate (not conferred by reading):** hand-trace the grouping loop on a novel input; explain each `Finding` field's evidence class; articulate M2 and its fix; state honestly that suite execution status is unknown until run. Present the project as AI-assisted; your understanding is evidenced by the fixes you make, not by authorship of the initial generation.

**Hypothetical only:** ARP would require a second observation type and per-protocol analyzer behind a common `Finding`; larger files would require streaming the reader into bounded batches with partial results. Neither is needed now.

---

## 12. Context capsule for a mentor

- **Product/scope:** local DHCPv4 capture → conversation → findings workbench. Today: upload endpoint + unwired Core analyzer + console-logging Angular shell.
- **Source reviewed:** Api (except absent `PcapReader.cs`, described from last-known content), Core, Core.Tests, Angular `src/app`, manifests, README excerpts. Not inspected: lockfile detail, `angular.json`, tsconfigs, README lines 200–619.
- **Stack (configured):** .NET 10; SharpPcap 6.3.1 + PacketDotNet 1.4.8 (native libpcap/Npcap); xUnit 2.9.2; Angular 21.2 (not 20), RxJS 7.8, TS 5.9, vitest 4.
- **Architecture:** minimal API → (should call) Core `Reconstruct` → `Classify` → `Analyze`; Angular singleton services with hardcoded URLs.
- **Core algorithms:** identity string (cid > chaddr, suppressed when relayed) → group by xid then identity equality with 30 s attempt split → no-identity attach-if-single else unresolved; flow by message-type precedence; 8 rule codes.
- **Evidence semantics:** `Finding` separates Observed/Missing/Hypotheses/NotEvaluated; wording generally careful; "completed" label hides NAK.
- **Tests:** 17 unit tests over synthetic observations; execution not assessed; two predicted failures; two encode defects. No parser/HTTP/browser tests.
- **Highest-priority:** M1 build break; M2/M2b identity split (evidence corruption); M3 NAK unreachable; M4 test/wording; M5 README overclaims.
- **Study order:** Finding.cs → Program.cs → DhcpObservation.cs → ConversationReconstructor.cs → FlowClassifier/Analyzer → tests.
- **Unknowns:** SharpPcap native deps present?; actual suite pass/fail; PacketDotNet VLAN/pcapng behavior; frontend spec outcome.

**Omitted for length:** `angular.json`/tsconfig review, full README line-by-line comparison, representative JSON for every rule.
