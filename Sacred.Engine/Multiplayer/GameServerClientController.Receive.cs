using Sacred.Network.Admission;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal sealed partial class GameServerClientController
{
    private void Receive(GoldGameEvent gameEvent)
    {
        _gameplay.Apply(gameEvent);
        AdvanceCreatureControlCallbacks();
        var opcode = gameEvent.Message.Opcode;
        _opcodeCounts.TryGetValue(opcode, out var count);
        _opcodeCounts[opcode] = count + 1;
        _eventsReceived++;
        // Bounded diagnostics until native object admission/removal and reference mappings are implemented.
        switch (gameEvent)
        {
            case GoldSessionReceiveControlEvent control:
                EngineLog.WriteLine($"Network native receive control: {control.Enabled}.");
                break;
            case GoldLobbyEvent lobby:
                EngineLog.WriteLine($"Network lobby 0x{lobby.Message.Opcode:X3}: " + (lobby switch
                {
                    GoldLobbyProfileEvent profile => $"profile {profile.Value}: {profile.Text}",
                    GoldLobbyWindowEvent window => $"window value {window.Value}: {window.Text}",
                    GoldLobbyChannelsEvent channels => $"{channels.Rows.Count} channels received",
                    GoldLobbyServerEvent server => $"server key {server.Server.EntryKey}, window {server.Server.WindowValue}; {WorldReplicas.Session.Lobby.ServerBrowser.Servers.Count} browser rows",
                    GoldLobbyPlayerRefreshEvent refresh => $"player refresh {refresh.Value}: {refresh.Text}; requests {WorldReplicas.Session.Lobby.ServerBrowser.RefreshRequestCount}",
                    _ => "update received"
                }));
                break;
            case GoldSessionNoticeEvent notice:
                var view = WorldReplicas.Session.Notices.LastReceipt!;
                EngineLog.WriteLine($"Network notice 0x{notice.Message.Opcode:X3}: " +
                    (view.Text ?? $"[{notice.LocalizationKey}] {notice.Text}") +
                    (notice.ErrorCode is { } error ? $" (native error {error})" : ""));
                break;
            case GoldChannelChatEvent channelChat:
                if (WorldReplicas.Session.ChannelChat.LastDelivery is { } delivery)
                    EngineLog.WriteLine($"Network {channelChat.Channel} chat: " +
                        (delivery.SpeakerName is { } name ? $"{name}: {channelChat.Text}" : channelChat.Text));
                break;
            case GoldClientHostRoleEvent role:
                EngineLog.WriteLine($"Network client-host role: {role.IsClientHost}; " +
                    $"{WorldReplicas.Session.ClientHostRole.ChangeCount} role changes. Host world handoff remains incomplete.");
                break;
            case GoldChatEvent chat:
                EngineLog.WriteLine($"Network chat{(chat.IsPrivate ? " (private)" : "")}: " +
                    (chat.IsServerMessage ? chat.Text : $"{chat.Name}: {chat.Text}"));
                break;
            case GoldCheatsEnabledEvent cheats:
                EngineLog.WriteLine($"Network cheats {(cheats.Enabled ? "enabled" : "disabled")} by GameServer.");
                break;
            case GoldScriptGroupEvent group:
                EngineLog.WriteLine($"Network script group: selector {group.Selector}, action {group.Action}, group {group.Group}, member {group.Member}; " +
                    $"{WorldReplicas.WorldBanks.ScriptGroups.Groups.Count} groups retained.");
                break;
            case GoldInactiveReceiveHookEvent when count == 0:
                EngineLog.WriteLine($"Network dispatch 0x{opcode:X3}: original Gold receive hook is empty; ignoring payload.");
                break;
            case GoldObjectCallbackCleanupEvent cleanup:
                EngineLog.WriteLine($"Network callback cleanup: ref {cleanup.ObjectReference}; " +
                    $"{WorldReplicas.DeathScripts.Registrations.Count} registrations retained, {WorldReplicas.DeathScripts.RemovedRegistrationCount} removed this session.");
                break;
            case GoldSessionPauseEvent pause:
                EngineLog.WriteLine($"Network session {(pause.IsPaused ? "paused" : "resumed")}; origin {pause.Message.Origin}.");
                break;
            case GoldTeamEvent team:
                EngineLog.WriteLine($"Network team packet 0x{team.Message.Opcode:X3}, origin {team.Message.Origin}; " +
                    $"local team {WorldReplicas.Session.Teams.GetTeam(WorldReplicas.Session.Teams.LocalPlayerSlot)}, " +
                    $"outgoing invitations 0x{WorldReplicas.Session.Teams.OutgoingInvitations:X8}, incoming 0x{WorldReplicas.Session.Teams.IncomingInvitations:X8}.");
                break;
            case GoldSessionPlayerLimitEvent limit:
                EngineLog.WriteLine($"Network player capacity: announced {limit.AnnouncedMaxPlayers}; effective {WorldReplicas.Session.MaxPlayers?.ToString() ?? "unknown"}.");
                break;
            case GoldSessionLockEvent session:
                EngineLog.WriteLine($"Network session {(session.IsLocked ? "locked" : "unlocked")}.");
                break;
            case GoldSessionGameFlagsEvent gameFlags:
                EngineLog.WriteLine($"Network game flags: 0x{gameFlags.Flags:X8}; session lock {WorldReplicas.Session.IsLocked}; native control word {WorldReplicas.Session.NativeControlWord}.");
                break;
            case GoldCreatureTextEvent text:
                EngineLog.WriteLine($"Network creature text: ref {text.ObjectReference}, text resource {text.TextId}; accepted {WorldReplicas.Combat.TextEvents}.");
                break;
            case GoldWorldRegionValueEvent region:
                EngineLog.WriteLine($"Network region value: region {region.RegionIndex}, selector {region.ValueKind}, {(region.Add ? "add" : "set")} unsigned value {region.RawValue}.");
                break;
            case GoldObjectQuestEvent quest:
                EngineLog.WriteLine($"Network quest {(quest.IsEffect ? "effect" : "number")}: ref {quest.ObjectReference}, value {quest.Value}, raw 0x{quest.RawValue:X8}.");
                break;
            case GoldCreatureDialogEvent dialog:
                EngineLog.WriteLine($"Network dialog: ref {dialog.ObjectReference}, selector {unchecked((int)dialog.DialogIndex)}, object flags 0x{dialog.ObjectFlags:X8}; updates {WorldReplicas.CreatureControls.DialogUpdates}.");
                break;
            case GoldCreatureFightModeEvent fight:
                EngineLog.WriteLine($"Network fight mode: ref {fight.ObjectReference}, enabled {fight.Enabled}; updates {WorldReplicas.CreatureControls.FightModeUpdates}.");
                break;
            case GoldPlayerKickEvent kick:
                EngineLog.WriteLine($"Network native kick: sender {kick.Message.Sender}, origin {kick.Message.Origin}; " +
                    $"{kick.Description} (reason {kick.Reason}); disconnects this client {kick.DisconnectsClient}.");
                break;
            case GoldObjectFlightEvent flight:
                WorldReplicas.Objects.TryGetValue(flight.ObjectReference, out var flyingObject);
                EngineLog.WriteLine($"Network item flight: ref {flight.ObjectReference}, opcode 0x{flight.Message.Opcode:X3}, sender {flight.Message.Sender}, origin {flight.Message.Origin}, owner {flyingObject?.BaseState.Owner}; applied {flyingObject?.Flight == flight}; arc {flight.IsItemArc}, height {flight.InitialHeight}.");
                break;
            case GoldAdmissionEvent admission:
                _admission = $"Sacred slot {admission.PlayerSlot}, flags 0x{admission.Flags:X2}; initial character/world import pending";
                EngineLog.WriteLine($"Network admission: {_admission}.");
                EngineLog.WriteLine($"Network startup{StartupStatus}; hero/inventory upload pending.");
                break;
            case GoldRejectionEvent rejection:
                _admission = $"rejected: reason {rejection.Reason}, data mismatch mask 0x{rejection.DataMismatchMask:X8}";
                EngineLog.WriteLine($"Network admission: {_admission}.");
                _connection?.Stop();
                break;
            case GoldPlayerDataEvent playerData:
                EngineLog.WriteLine($"Network player information: character type {playerData.PlayerData.CharacterType}, version {playerData.PlayerData.Version}; profile import pending.");
                break;
            case GoldHostWorldAvailableEvent:
                EngineLog.WriteLine($"Network host world available: origin {gameEvent.Message.Origin}; participant hero/inventory upload pending.");
                break;
            case GoldParticipantStartApprovedEvent:
                _admission = $"Sacred slot {_connection?.Admission.PlayerSlot}, flags 0x{_connection?.Admission.Flags:X2}; participant approved; scene binding pending";
                EngineLog.WriteLine("Network participant start approved by GameServer; scene binding pending.");
                break;
            case GoldSectorEntryEvent sectors:
                EngineLog.WriteLine($"Network sector entry: {sectors.Entry.Count} retained slots; use set server sectors; world/script application pending.");
                break;
            case GoldPlayerRosterEvent roster:
                EngineLog.WriteLine($"Network server roster: {roster.Roster.Count} retained records; server metadata at index 0, player slots 1..16; actor/world binding pending.");
                break;
            case GoldTimeProbeEvent:
                EngineLog.WriteLine($"Network time probe: {(_connection?.Admission.Phase == GoldAdmissionPhase.SlotAssigned ? "transport handles native reply; counters in set server callbacks" : "retained in transport diagnostics")}.");
                break;
            case GoldReferenceBatchEvent references:
                ObjectReferences.Apply(references.Batch);
                foreach (var reference in references.Batch.References)
                    if (_referenceSet.Add(reference)) _allocatedReferences.Add(reference);
                EngineLog.WriteLine($"Network allocated object references: {references.Batch.Count}; hero/player references are separate; world import pending.");
                break;
            case GoldReferenceRangeEvent range:
                EngineLog.WriteLine($"Network world-trigger reference range: {range.Start} + {range.Count}; {WorldReplicas.TriggerReferences.Available} IDs available; authored trigger creation pending.");
                break;
            case GoldPasswordResultEvent password:
                EngineLog.WriteLine($"Network game password: {(password.Accepted ? "accepted" : "rejected")}; character/world entry pending.");
                break;
            case GoldMovementEvent movement when _movements.ContainsKey(movement.ObjectReference) || _movements.Count < 4096:
                _movements[movement.ObjectReference] = [movement.Word1, movement.Word2, movement.Word3, movement.Word4, movement.Word5];
                break;
            case GoldMovementEndEvent end:
                _movements.Remove(end.ObjectReference);
                break;
            case GoldObjectRemovalEvent:
            case GoldObjectOwnerEvent:
                GameServerObjectLifecycleDiagnostics.Apply(_movements, _doors, gameEvent);
                break;
            case GoldObjectPositionEvent position when position.Message.Opcode != GoldCreaturePositionCorrectionCodec.Opcode:
                _movements.Remove(position.ObjectReference);
                break;
            case GoldIdleEvent idle:
                _movements.Remove(idle.ObjectReference);
                break;
            case GoldDoorEvent door when _doors.ContainsKey(door.ObjectReference) || _doors.Count < 4096:
                _doors[door.ObjectReference] = door.Open;
                EngineLog.WriteLine($"Network door receive: object {door.ObjectReference}, requested {(door.Open ? "open" : "closed")}; " +
                    $"{WorldReplicas.DoorInteractions.Pending.Count} pending native activations. Inspect set server doors.");
                break;
            case GoldContainerActivationEvent container:
                EngineLog.WriteLine($"Network container receive: object {container.ObjectReference}; applied {WorldReplicas.ContainerInteractions.AppliedCount}, " +
                    $"already activated {WorldReplicas.ContainerInteractions.AlreadyActivatedCount}, pending {WorldReplicas.ContainerInteractions.Pending.Count}.");
                break;
            case GoldInventoryEvent inventory when _inventories.ContainsKey((inventory.Message.Sender, inventory.Message.Origin, inventory.InventoryId)) || _inventories.Count < 64:
                _inventories[(inventory.Message.Sender, inventory.Message.Origin, inventory.InventoryId)] = inventory;
                break;
            case GoldObjectEvent obj when _objects.ContainsKey(obj.ObjectReference) || _objects.Count < 4096:
                _objects[obj.ObjectReference] = obj;
                break;
        }
        if (count == 0)
            EngineLog.WriteLine($"Network dispatch: 0x{opcode:X4}, {gameEvent.GetType().Name}, {gameEvent.Message.Payload.Length} bytes; retained for admission/reference mapping.");
    }

}
