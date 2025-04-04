using Content.Server.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Humanoid;
using Content.Shared.Maps;
using Content.Shared.Projectiles;
using Content.Shared.Throwing;
using Robust.Shared.Audio;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using System.Numerics;

namespace Content.Server.Atmos.EntitySystems;

public sealed partial class AtmosphereSystem
{
    private void UpdateHighPressure(float frameTime)
    {
        base.Update(frameTime);
        var pressureQuery = EntityQueryEnumerator<MovedByPressureComponent, PhysicsComponent>();

        while (pressureQuery.MoveNext(out var uid, out var pressure, out var physics))
        {
            if (!pressure.Throwing || _gameTiming.CurTime < pressure.ThrowingCutoffTarget)
                continue;

            if (TryComp(uid, out ThrownItemComponent? thrown))
            {
                _thrown.LandComponent(uid, thrown, physics, true);
                _thrown.StopThrow(uid, thrown);
            }

            _physics.SetBodyStatus(uid, physics, BodyStatus.OnGround);
            _physics.SetSleepingAllowed(uid, physics, true);

            pressure.Throwing = false;
        }
    }

    private void HighPressureMovements(Entity<GridAtmosphereComponent> gridAtmosphere,
        TileAtmosphere tile,
        EntityQuery<PhysicsComponent> bodies,
        EntityQuery<TransformComponent> xforms,
        EntityQuery<MovedByPressureComponent> pressureQuery,
        EntityQuery<MetaDataComponent> metas,
        EntityQuery<ProjectileComponent> projectileQuery,
        double gravity)
    {
        var atmosComp = gridAtmosphere.Comp;
        var oneAtmos = Atmospherics.OneAtmosphere;

        // No atmos yeets, return early.
        if (!SpaceWind
            || !gridAtmosphere.Comp.SpaceWindSimulation // Is the grid marked as exempt from space wind?
            || tile.Air is null || tile.Space // No Air Checks. Pressure differentials can't exist in a hard vacuum.
            || tile.Air.Pressure <= atmosComp.PressureCutoff // Below 5kpa(can't throw a base item)
            || oneAtmos - atmosComp.PressureCutoff <= tile.Air.Pressure
            && tile.Air.Pressure <= oneAtmos + atmosComp.PressureCutoff // Check within 5kpa of default pressure.
            || !TryComp(gridAtmosphere.Owner, out MapGridComponent? mapGrid)
            || !_mapSystem.TryGetTileRef(gridAtmosphere.Owner, mapGrid, tile.GridIndices, out var tileRef))
            return;

        var tileDef = (ContentTileDefinition) _tileDefinitionManager[tileRef.Tile.TypeId];
        if (!tileDef.SimulatedTurf)
            return;

        var partialFrictionComposition = gravity * tileDef.MobFrictionNoInput;

        var pressureVector = GetPressureVectorFromTile(gridAtmosphere, tile);
        if (!pressureVector.IsValid())
            return;
        tile.LastPressureDirection = pressureVector;

        // Calculate this HERE so that we aren't running the square root of a whole Newton vector per item.
        var pVecLength = pressureVector.Length();
        if (pVecLength <= 1) // Then guard against extremely small vectors.
            return;

        pressureVector *= SpaceWindStrengthMultiplier;

        if (pVecLength > 15 && !tile.Hotspot.Valid && atmosComp.SpaceWindSoundCooldown == 0)
        {
            var coordinates = _mapSystem.ToCenterCoordinates(tile.GridIndex, tile.GridIndices);
            var volume = Math.Clamp(pVecLength / atmosComp.SpaceWindSoundDenominator, atmosComp.SpaceWindSoundMinVolume, atmosComp.SpaceWindSoundMaxVolume);
            _audio.PlayPvs(atmosComp.SpaceWindSound, coordinates, AudioParams.Default.WithVariation(0.125f).WithVolume(volume));
        }

        if (atmosComp.SpaceWindSoundCooldown++ > atmosComp.SpaceWindSoundCooldownCycles)
            atmosComp.SpaceWindSoundCooldown = 0;

        // TODO: Deprecated for now, it sucks ass and I'm disassembling monstermos because it sucks. This'll be handled by Space Wind after I'm done whiteboarding better equations for it.
        // - TCJ
        // HandleDecompressionFloorRip(mapGrid, otherTile, otherTile.PressureDifference);

        _entSet.Clear();
        _lookup.GetLocalEntitiesIntersecting(tile.GridIndex, tile.GridIndices, _entSet, 0f);

        foreach (var entity in _entSet)
        {
            // Ideally containers would have their own EntityQuery internally or something given recursively it may need to slam GetComp<T> anyway.
            // Also, don't care about static bodies (but also due to collisionwakestate can't query dynamic directly atm).
            if (!bodies.TryGetComponent(entity, out var body)
                || !pressureQuery.TryGetComponent(entity, out var pressure)
                || !pressure.Enabled
                || _containers.IsEntityInContainer(entity, metas.GetComponent(entity))
                || pressure.LastHighPressureMovementAirCycle >= gridAtmosphere.Comp.UpdateCounter)
                continue;

            // tl;dr YEET
            ExperiencePressureDifference(
                (entity, pressure),
                gridAtmosphere.Comp.UpdateCounter,
                pressureVector,
                pVecLength,
                partialFrictionComposition,
                projectileQuery,
                xforms.GetComponent(entity),
                body);
        }
    }

    // Called from AtmosphereSystem.LINDA.cs with SpaceWind CVar check handled there.
    private void ConsiderPressureDifference(GridAtmosphereComponent gridAtmosphere, TileAtmosphere tile) => gridAtmosphere.HighPressureDelta.Add(tile);

    public void ExperiencePressureDifference(Entity<MovedByPressureComponent> ent,
        int cycle,
        Vector2 pressureVector,
        float pVecLength,
        double partialFrictionComposition,
        EntityQuery<ProjectileComponent> projectileQuery,
        TransformComponent? xform = null,
        PhysicsComponent? physics = null)
    {
        var (uid, component) = ent;
        if (!Resolve(uid, ref physics, false)
            || !Resolve(uid, ref xform)
            || physics.BodyType == BodyType.Static
            || physics.LinearVelocity.Length() >= SpaceWindMaxForce)
            return;

        var alwaysThrow = partialFrictionComposition == 0 || physics.BodyStatus == BodyStatus.InAir;

        // Coefficient of static friction in Newtons (kg * m/s^2), which might not apply under certain conditions.
        var coefficientOfFriction = partialFrictionComposition * physics.Mass;
        coefficientOfFriction *= _standingSystem.IsDown(uid) ? 3 : 1;

        if (HasComp<HumanoidAppearanceComponent>(ent))
            pressureVector *= HumanoidThrowMultiplier;
        if (!alwaysThrow && pVecLength < coefficientOfFriction)
            return;

        // Yes this technically increases the magnitude by a small amount... I detest having to swap between "World" and "Local" vectors.
        // ThrowingSystem increments linear velocity by a given vector, but we have to do this anyways because reasons.
        var velocity = _transformSystem.GetWorldRotation(uid).ToWorldVec() + pressureVector;

        // _sharedStunSystem.TryKnockdown(uid, TimeSpan.FromSeconds(SpaceWindKnockdownTime), false);
        _throwing.TryThrow(uid, velocity, physics, xform, projectileQuery,
            1, doSpin: physics.AngularVelocity < SpaceWindMaxAngularVelocity);

        component.LastHighPressureMovementAirCycle = cycle;
        component.Throwing = true;
        component.ThrowingCutoffTarget = _gameTiming.CurTime + component.CutoffTime;
    }
}
