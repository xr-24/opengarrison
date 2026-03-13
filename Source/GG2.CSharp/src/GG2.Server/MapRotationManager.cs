using System;
using System.Collections.Generic;
using System.Linq;
using GG2.Core;
using static ServerHelpers;

sealed class MapRotationManager
{
    private readonly SimulationWorld _world;
    private readonly Action<string> _log;
    private readonly List<string> _mapRotation;
    private int _mapRotationIndex;

    public MapRotationManager(SimulationWorld world, string? requestedMap, string? mapRotationFile, IReadOnlyList<string> stockMapRotation, Action<string> log)
    {
        _world = world;
        _log = log;

        var rotation = LoadMapRotation(mapRotationFile, stockMapRotation);
        if (rotation.Count == 0)
        {
            rotation = SimpleLevelFactory.GetAvailableSourceLevels()
                .Select(entry => entry.Name)
                .ToList();
        }

        _mapRotation = rotation;
        InitializeWorldLevel(requestedMap);
    }

    public bool TryApplyPendingMapChange()
    {
        return ServerHelpers.TryApplyPendingMapChange(_world, _mapRotation, ref _mapRotationIndex, _log);
    }

    private void InitializeWorldLevel(string? requestedMap)
    {
        if (!string.IsNullOrWhiteSpace(requestedMap))
        {
            var loadedRequestedMap = _world.TryLoadLevel(requestedMap, mapAreaIndex: 1, preservePlayerStats: false);
            if (!loadedRequestedMap)
            {
                _log($"[server] unknown map \"{requestedMap}\"; falling back to {_world.Level.Name}.");
            }

            _mapRotationIndex = EnsureMapRotationIndex(
                _mapRotation,
                loadedRequestedMap ? requestedMap : _world.Level.Name,
                _world.Level.Name);
            return;
        }

        if (_mapRotation.Count == 0)
        {
            _mapRotationIndex = 0;
            return;
        }

        var requestedRotationMap = _mapRotation[0];
        if (!_world.TryLoadLevel(requestedRotationMap, mapAreaIndex: 1, preservePlayerStats: false))
        {
            _log($"[server] unknown map \"{requestedRotationMap}\"; falling back to {_world.Level.Name}.");
        }

        _mapRotationIndex = Math.Max(0, FindMapRotationIndex(_mapRotation, _world.Level.Name));
    }
}
