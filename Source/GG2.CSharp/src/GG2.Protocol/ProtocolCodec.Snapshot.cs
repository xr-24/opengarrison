using System.Collections.Generic;
using System.IO;

namespace GG2.Protocol;

public static partial class ProtocolCodec
{
    private static void WriteSnapshot(BinaryWriter writer, SnapshotMessage snapshot)
    {
        writer.Write(snapshot.Frame);
        writer.Write(snapshot.TickRate);
        WriteString(writer, snapshot.LevelName, MaxLevelNameBytes, nameof(snapshot.LevelName));
        writer.Write(snapshot.MapAreaIndex);
        writer.Write(snapshot.MapAreaCount);
        writer.Write(snapshot.GameMode);
        writer.Write(snapshot.MatchPhase);
        writer.Write(snapshot.WinnerTeam);
        writer.Write(snapshot.TimeRemainingTicks);
        writer.Write(snapshot.RedCaps);
        writer.Write(snapshot.BlueCaps);
        writer.Write(snapshot.SpectatorCount);
        writer.Write(snapshot.LastProcessedInputSequence);
        WriteIntelState(writer, snapshot.RedIntel);
        WriteIntelState(writer, snapshot.BlueIntel);
        WriteSnapshotPlayers(writer, snapshot.Players);
        WriteCombatTraces(writer, snapshot.CombatTraces);
        WriteSentryStates(writer, snapshot.Sentries);
        WriteShotStates(writer, snapshot.Shots);
        WriteShotStates(writer, snapshot.Bubbles);
        WriteShotStates(writer, snapshot.Blades);
        WriteShotStates(writer, snapshot.Needles);
        WriteShotStates(writer, snapshot.RevolverShots);
        WriteRocketStates(writer, snapshot.Rockets);
        WriteFlameStates(writer, snapshot.Flames);
        WriteMineStates(writer, snapshot.Mines);
        WritePlayerGibStates(writer, snapshot.PlayerGibs);
        WriteBloodDropStates(writer, snapshot.BloodDrops);
        WriteDeadBodyStates(writer, snapshot.DeadBodies);
        writer.Write(snapshot.ControlPointSetupTicksRemaining);
        WriteControlPointStates(writer, snapshot.ControlPoints);
        WriteGeneratorStates(writer, snapshot.Generators);
        WriteDeathCamState(writer, snapshot.LocalDeathCam);
        WriteKillFeedEntries(writer, snapshot.KillFeed);
        WriteVisualEvents(writer, snapshot.VisualEvents);
        WriteSoundEvents(writer, snapshot.SoundEvents);
    }

    private static SnapshotMessage ReadSnapshot(BinaryReader reader)
    {
        var frame = reader.ReadUInt64();
        var tickRate = reader.ReadInt32();
        var levelName = ReadString(reader, MaxLevelNameBytes);
        var mapAreaIndex = reader.ReadByte();
        var mapAreaCount = reader.ReadByte();
        var gameMode = reader.ReadByte();
        var matchPhase = reader.ReadByte();
        var winnerTeam = reader.ReadByte();
        var timeRemainingTicks = reader.ReadInt32();
        var redCaps = reader.ReadInt32();
        var blueCaps = reader.ReadInt32();
        var spectatorCount = reader.ReadInt32();
        var lastProcessedInputSequence = reader.ReadUInt32();
        var redIntel = ReadIntelState(reader);
        var blueIntel = ReadIntelState(reader);
        var players = ReadSnapshotPlayers(reader);
        var combatTraces = ReadCombatTraces(reader);
        var sentries = ReadSentryStates(reader);
        var shots = ReadShotStates(reader);
        var bubbles = ReadShotStates(reader);
        var blades = ReadShotStates(reader);
        var needles = ReadShotStates(reader);
        var revolverShots = ReadShotStates(reader);
        var rockets = ReadRocketStates(reader);
        var flames = ReadFlameStates(reader);
        var mines = ReadMineStates(reader);
        var playerGibs = ReadPlayerGibStates(reader);
        var bloodDrops = ReadBloodDropStates(reader);
        var deadBodies = ReadDeadBodyStates(reader);
        var controlPointSetupTicksRemaining = reader.ReadInt32();
        var controlPoints = ReadControlPointStates(reader);
        var generators = ReadGeneratorStates(reader);
        var deathCam = ReadDeathCamState(reader);
        var killFeed = ReadKillFeedEntries(reader);
        var visualEvents = ReadVisualEvents(reader);
        var soundEvents = ReadSoundEvents(reader);

        return new SnapshotMessage(
            frame,
            tickRate,
            levelName,
            mapAreaIndex,
            mapAreaCount,
            gameMode,
            matchPhase,
            winnerTeam,
            timeRemainingTicks,
            redCaps,
            blueCaps,
            spectatorCount,
            lastProcessedInputSequence,
            redIntel,
            blueIntel,
            players,
            combatTraces,
            sentries,
            shots,
            bubbles,
            blades,
            needles,
            revolverShots,
            rockets,
            flames,
            mines,
            playerGibs,
            bloodDrops,
            deadBodies,
            controlPointSetupTicksRemaining,
            controlPoints,
            generators,
            deathCam,
            killFeed,
            visualEvents,
            soundEvents);
    }

    private static void WriteSnapshotPlayers(BinaryWriter writer, IReadOnlyList<SnapshotPlayerState> players)
    {
        writer.Write((byte)players.Count);
        for (var index = 0; index < players.Count; index += 1)
        {
            var player = players[index];
            writer.Write(player.Slot);
            writer.Write(player.PlayerId);
            WriteString(writer, player.Name, MaxPlayerNameBytes, nameof(player.Name));
            writer.Write(player.Team);
            writer.Write(player.ClassId);
            writer.Write(player.IsAlive);
            writer.Write(player.IsAwaitingJoin);
            writer.Write(player.IsSpectator);
            writer.Write(player.RespawnTicks);
            writer.Write(player.X);
            writer.Write(player.Y);
            writer.Write(player.HorizontalSpeed);
            writer.Write(player.VerticalSpeed);
            writer.Write(player.Health);
            writer.Write(player.MaxHealth);
            writer.Write(player.Ammo);
            writer.Write(player.MaxAmmo);
            writer.Write(player.Kills);
            writer.Write(player.Deaths);
            writer.Write(player.Caps);
            writer.Write(player.HealPoints);
            writer.Write(player.Metal);
            writer.Write(player.IsGrounded);
            writer.Write(player.IsCarryingIntel);
            writer.Write(player.IsSpyCloaked);
            writer.Write(player.IsUbered);
            writer.Write(player.IsHeavyEating);
            writer.Write(player.HeavyEatTicksRemaining);
            writer.Write(player.IsSniperScoped);
            writer.Write(player.SniperChargeTicks);
            writer.Write(player.FacingDirectionX);
            writer.Write(player.AimDirectionDegrees);
            writer.Write(player.IsTaunting);
            writer.Write(player.TauntFrameIndex);
            writer.Write(player.IsChatBubbleVisible);
            writer.Write(player.ChatBubbleFrameIndex);
            writer.Write(player.ChatBubbleAlpha);
        }
    }

    private static List<SnapshotPlayerState> ReadSnapshotPlayers(BinaryReader reader)
    {
        var playerCount = reader.ReadByte();
        var players = new List<SnapshotPlayerState>(playerCount);
        for (var index = 0; index < playerCount; index += 1)
        {
            players.Add(new SnapshotPlayerState(
                reader.ReadByte(),
                reader.ReadInt32(),
                ReadString(reader, MaxPlayerNameBytes),
                reader.ReadByte(),
                reader.ReadByte(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadInt16(),
                reader.ReadSingle(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadBoolean(),
                reader.ReadInt32(),
                reader.ReadBoolean(),
                reader.ReadInt32(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadBoolean(),
                reader.ReadSingle(),
                reader.ReadBoolean(),
                reader.ReadInt32(),
                reader.ReadSingle()));
        }

        return players;
    }
}
