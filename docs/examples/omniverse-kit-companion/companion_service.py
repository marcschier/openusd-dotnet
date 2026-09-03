"""SPECIFICATION EXAMPLE -- NOT A RUNNABLE MODULE.

This file is checked into marcschier/openusd2, the repository that owns the openusd.bridge.v1
wire contract (src/OpenUsd.Bridge.Protocol) and its C# client (OpenUsd.Bridge.Grpc). It sketches,
in Python pseudocode, the server-side behavior described by docs/omniverse-kit-companion.md and
eng/kit-companion-spec.json for a separately owned and separately distributed Omniverse Kit
extension. That extension does not exist in, and is not authorized for, this repository.

Why this cannot run:
  * It imports ``openusd_bridge_v1_pb2`` and ``openusd_bridge_v1_pb2_grpc``, placeholder names
    for the Python stubs a real companion would generate from this repository's shipped
    ``.proto`` files (see eng/kit-companion-spec.json -> pythonGrpcWorkflow). No such generated
    module is vendored, built, or published anywhere in this repository.
  * It imports ``omni.usd`` and ``omni.client``, Kit/Nucleus modules this repository never
    depends on, links against, or redistributes.
  * ``if __name__ == "__main__":`` raises immediately rather than attempting to start a server,
    so even a host with every real dependency installed cannot accidentally run this as the
    companion service.

Nothing here is evidence of an implementation. See docs/omniverse-kit-companion.md for the full,
implementation-ready specification this pseudocode illustrates, and
docs/omniverse-kit-companion-reference.g.md for the generated reference tables it is checked
against.
"""

from __future__ import annotations

import dataclasses
import time
from typing import Iterator

# Placeholder generated-stub imports. These modules do not exist in this repository or on any
# package index it publishes to; a real companion generates them at build time from
# src/OpenUsd.Bridge.Protocol/protos/openusd/bridge/v1/{wire,service}.proto.
import openusd_bridge_v1_pb2 as wire          # type: ignore[import-not-found]
import openusd_bridge_v1_pb2_grpc as service  # type: ignore[import-not-found]

# Placeholder Kit/Nucleus imports. Owned entirely by the companion side; never a dependency of
# this repository. See docs/omniverse-kit-companion.md "Kit-side ownership".
import omni.client  # type: ignore[import-not-found]
import omni.usd     # type: ignore[import-not-found]


# The one negotiated protocol identity this pseudocode enforces. Mirrors
# OpenUsd.Bridge.Protocol.BridgeProtocol -- see eng/kit-companion-spec.json "protocol" for the
# live-checked values, and never hardcode a different pair here.
PROTOCOL_PACKAGE = "openusd.bridge.v1"
PROTOCOL_MAJOR_VERSION = 1
REQUIRED_CAPABILITIES = (
    wire.CAPABILITY_FULL_SNAPSHOT,
    wire.CAPABILITY_ORDERED_DELTA,
)


class _ApplyFailed(Exception):
    """Raised when one or more Kit-side authoring calls in a batch did not complete
    successfully. No Kit API providing an atomic, rollback-safe batch apply has been
    evidenced, so a partial failure is handled by stopping deltas and forcing a resync
    (see PublishLocalBatch below), never by assuming a rollback happened."""


@dataclasses.dataclass
class _LedgerEntry:
    """One bounded idempotency-ledger entry: a sequence and a content fingerprint only -- a
    hash over epoch, origin_id, correlation_id, coalescing_key, and updates. Never the authored
    payload, and never a stored copy of the original Acknowledgement: a replay is answered with
    a freshly formed SESSION_OUTCOME_DUPLICATE Acknowledgement, not a cached one -- see
    eng/kit-companion-spec.json "idempotencyLedger.rules"."""

    sequence: int
    content_fingerprint: bytes


class CompanionServicePseudocode(service.LiveBridgeServicer):
    """Pseudocode shape of the Kit-side openusd.bridge.v1 server.

    Real responsibilities this class would own, per docs/omniverse-kit-companion.md:
      * omni:// resolution and Nucleus auth (via omni.client), never delegated back to a client.
      * The one live .live layer this bridge root is scoped to.
      * The bounded idempotency ledger keyed by PublishLocalBatchRequest.idempotency_key.
      * Exactly one authoritative remote origin per negotiated session -- no distributed merge.
    """

    def __init__(self, bridge_root_path: str) -> None:
        self._bridge_root_path = bridge_root_path
        self._session_id: str | None = None
        self._epoch = 0
        self._last_accepted_sequence = 0
        self._ledger: dict[str, _LedgerEntry] = {}
        self._ledger_order: list[str] = []  # bounds the ledger to replayWindowMaxLength

    # -- Negotiate --------------------------------------------------------------------------

    def Negotiate(self, request: "wire.HandshakeRequest", context) -> "wire.HandshakeResponse":
        # 1. Authenticate first. Never distinguish "missing" from "invalid" in the detail text.
        if not self._authenticate(context):
            return self._reject(wire.HANDSHAKE_REJECTION_UNAUTHENTICATED, "authentication failed")

        # 2. Major version before anything else: a mismatched peer's capabilities/limits do not
        #    mean what this version thinks they mean.
        if request.client_version.major != PROTOCOL_MAJOR_VERSION:
            return self._reject(wire.HANDSHAKE_REJECTION_VERSION, "unsupported major version")

        # 3. Required capabilities must be in the agreed intersection.
        agreed = [c for c in request.client_capabilities if c in self._supported_capabilities()]
        if not all(capability in agreed for capability in REQUIRED_CAPABILITIES):
            return self._reject(wire.HANDSHAKE_REJECTION_CAPABILITY, "missing required capability")

        # 4. Effective limits: element-wise minimum, and it must stay usable.
        effective_limits = self._intersect_limits(request.client_limits, self._local_limits())
        if not self._limits_usable(effective_limits):
            return self._reject(wire.HANDSHAKE_REJECTION_LIMITS, "limits not usable")

        # 5. Bridge root must match exactly -- never a sub-path or a parent path.
        if request.bridge_root_path != self._bridge_root_path:
            return self._reject(wire.HANDSHAKE_REJECTION_BRIDGE_ROOT, "bridge root mismatch")

        # 6. Resume a live requested session, or mint a fresh epoch. Never accept without one.
        epoch = self._resume_or_mint_epoch(request.requested_session_id)

        return wire.HandshakeResponse(
            accepted=True,
            server_version=wire.ProtocolVersion(major=PROTOCOL_MAJOR_VERSION, minor=0),
            server_capabilities=agreed,
            epoch=epoch,
            bridge_root_path=self._bridge_root_path,
            effective_limits=effective_limits,
            rejection=wire.HANDSHAKE_REJECTION_NONE,
        )

    # -- GetSnapshot (read-only, retry-safe) -------------------------------------------------

    def GetSnapshot(self, request: "wire.SnapshotRequest", context) -> "wire.StageSnapshot":
        # A complete, self-contained overlay from empty -- never a diff against a prior
        # snapshot -- bounded by MaxBridgeOverlayUpdates / MaxBridgeOverlayPayloadBytes.
        updates = self._read_bounded_overlay_updates()
        return wire.StageSnapshot(
            epoch=self._current_session_epoch(),
            sequence=self._last_accepted_sequence,
            bridge_root_path=self._bridge_root_path,
            updates=updates,
        )

    # -- GetStatus (read-only, retry-safe) ---------------------------------------------------

    def GetStatus(self, request: "wire.StatusRequest", context) -> "wire.SessionStatus":
        # Every non-optional scalar field below must be tracked and reported accurately: proto3
        # non-optional scalars have no wire-level absent state, so an untracked counter would be
        # indistinguishable from an accurately reported zero. Never a placeholder or estimate.
        return self._current_status()

    # -- PublishLocalBatch (never retried by the transport; idempotent by key) --------------

    def PublishLocalBatch(
        self, request: "wire.PublishLocalBatchRequest", context
    ) -> "wire.Acknowledgement":
        # 1. Idempotency ledger lookup happens before anything else is touched. The ledger
        #    never stores the original Acknowledgement -- only a sequence and a fingerprint --
        #    so a replay is always answered with a freshly formed Duplicate acknowledgement,
        #    never the first, SESSION_OUTCOME_APPLIED response returned unchanged.
        existing = self._ledger.get(request.idempotency_key)
        if existing is not None:
            fingerprint = self._fingerprint(request)
            if fingerprint == existing.content_fingerprint:
                return wire.Acknowledgement(
                    outcome=wire.SESSION_OUTCOME_DUPLICATE,
                    rejection=wire.SESSION_REJECTION_NONE,
                    sequence=existing.sequence,
                    # correlation_id is necessarily identical to the original request's, since
                    # the fingerprint (which covers correlation_id) matched.
                    correlation_id=request.correlation_id,
                    state=self._current_session_state(),
                )
            return self._acknowledge_rejected(
                request, wire.SESSION_REJECTION_DUPLICATE_CONFLICT
            )

        # 2. Epoch / sequence / scope / budget checks, in that order, before any apply.
        if not self._epoch_current(request.epoch):
            return self._acknowledge_rejected(request, wire.SESSION_REJECTION_EPOCH_RETIRED)
        if not self._sequence_is_next(request.sequence, request.epoch):
            return self._acknowledge_rejected(request, wire.SESSION_REJECTION_SEQUENCE_GAP)
        if not self._all_updates_in_scope(request.updates, self._bridge_root_path):
            return self._acknowledge_rejected(request, wire.SESSION_REJECTION_BRIDGE_SCOPE)
        if not self._overlay_budget_ok(request.updates):
            return self._acknowledge_rejected(request, wire.SESSION_REJECTION_OVERLAY_BUDGET)

        # 3. Issue the Kit-side authoring calls; only after every call in the batch has
        #    completed successfully does the server record the ledger entry and ack
        #    SESSION_OUTCOME_APPLIED. No atomic-transaction or Nucleus-durability guarantee is
        #    assumed here: neither has been evidenced for any Kit API this pseudocode targets.
        try:
            self._apply_to_live_layer(request.updates)
        except _ApplyFailed:
            # 3a. Any partial/failed apply stops further deltas and forces a full resync
            #     instead of guessing at a rollback the underlying API never promised.
            self._stop_emitting_deltas()
            self._send_resync_required(wire.RESYNC_REASON_APPLY_FAILED)
            return self._acknowledge_rejected(request, wire.SESSION_REJECTION_APPLY_FAILED)

        acknowledgement = wire.Acknowledgement(
            outcome=wire.SESSION_OUTCOME_APPLIED,
            rejection=wire.SESSION_REJECTION_NONE,
            sequence=request.sequence,
        )
        # The ledger records only the sequence and fingerprint, never this Acknowledgement
        # itself: a later replay is answered with a freshly formed Duplicate response instead.
        self._record_ledger_entry(request.idempotency_key, request.sequence,
                                   self._fingerprint(request))

        # 4. Echo request.origin_id on the delta this produces so the publisher's own
        #    connection can suppress it as an echo instead of reapplying it.
        self._enqueue_delta_for_subscribers(request, echo_origin_id=request.origin_id)
        return acknowledgement

    # -- StreamChanges (bidirectional; backpressure, resync, clean shutdown) ----------------

    def StreamChanges(
        self, request_iterator: Iterator["wire.ChangeStreamRequest"], context
    ) -> Iterator["wire.ChangeStreamMessage"]:
        # The first frame must be a handshake; anything else first is a protocol violation.
        first_request = next(request_iterator)
        if not first_request.HasField("handshake"):
            context.abort(code=None, details="first frame must be a handshake")
        handshake_response = self.Negotiate(first_request.handshake, context)
        yield wire.ChangeStreamMessage(handshake=handshake_response)
        if not handshake_response.accepted:
            return

        # Exactly one snapshot before any delta.
        yield wire.ChangeStreamMessage(snapshot=self.GetSnapshot(wire.SnapshotRequest(), context))

        for outbound in self._drain_bounded_delta_queue():
            if self._queue_depth_exceeds_backpressure_bound():
                # Never silently drop or reorder: require a resync instead.
                yield wire.ChangeStreamMessage(
                    resync_required=wire.ResyncRequired(
                        reason=wire.RESYNC_REASON_SEQUENCE_GAP
                    )
                )
                continue
            if outbound.origin_id == self._local_connection_origin_id(context):
                continue  # loop suppression: never echo a connection's own edit back to it
            yield wire.ChangeStreamMessage(delta=outbound)

        # Clean shutdown: stop admitting new work, drain in-flight publishes, then close.

    # -- helpers (pseudocode only; every body below is illustrative, not implemented) --------

    def _authenticate(self, context) -> bool: ...
    def _supported_capabilities(self) -> list: ...
    def _local_limits(self) -> "wire.Limits": ...
    def _intersect_limits(self, a: "wire.Limits", b: "wire.Limits") -> "wire.Limits": ...
    def _limits_usable(self, limits: "wire.Limits") -> bool: ...
    def _resume_or_mint_epoch(self, requested_session_id: str | None) -> "wire.SessionEpoch": ...
    def _current_session_epoch(self) -> "wire.SessionEpoch": ...
    def _read_bounded_overlay_updates(self) -> list: ...
    def _current_status(self) -> "wire.SessionStatus": ...
    def _current_session_state(self) -> "wire.SessionState": ...
    def _fingerprint(self, request) -> bytes: ...
    def _epoch_current(self, epoch: "wire.SessionEpoch") -> bool: ...
    def _sequence_is_next(self, sequence: int, epoch: "wire.SessionEpoch") -> bool: ...
    def _all_updates_in_scope(self, updates: list, bridge_root_path: str) -> bool: ...
    def _overlay_budget_ok(self, updates: list) -> bool: ...
    def _apply_to_live_layer(self, updates: list) -> None: ...
    def _record_ledger_entry(
        self, idempotency_key: str, sequence: int, content_fingerprint: bytes
    ) -> None: ...
    def _stop_emitting_deltas(self) -> None: ...
    def _send_resync_required(self, reason) -> None: ...
    def _enqueue_delta_for_subscribers(self, request, echo_origin_id: str) -> None: ...
    def _acknowledge_rejected(self, request, rejection) -> "wire.Acknowledgement": ...
    def _drain_bounded_delta_queue(self) -> Iterator["wire.StageDelta"]: ...
    def _queue_depth_exceeds_backpressure_bound(self) -> bool: ...
    def _local_connection_origin_id(self, context) -> str: ...

    def _reject(self, rejection, detail: str) -> "wire.HandshakeResponse":
        return wire.HandshakeResponse(accepted=False, rejection=rejection, detail=detail[:256])


if __name__ == "__main__":
    raise RuntimeError(
        "companion_service.py is a checked specification example, not a runnable service. "
        "See docs/omniverse-kit-companion.md and docs/examples/omniverse-kit-companion/README.md."
    )
