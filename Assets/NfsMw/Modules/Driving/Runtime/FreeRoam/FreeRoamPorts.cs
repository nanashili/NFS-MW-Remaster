using System.Collections.Generic;
using UnityEngine;

namespace NfsMwRemaster.Driving
{
    public enum FreeRoamState { Driving, Paused, Location, Event, Results, EventLoading }
    public enum WorldLocationKind { Safehouse, Garage, BodyShop, PerformanceShop, CarShow, PoliceStation }
    public enum FreeRoamEventKind { Sprint, Circuit, Drag, Speedtrap, Pursuit }

    public interface IWorldLocation
    {
        string Id { get; }
        string DisplayName { get; }
        WorldLocationKind Kind { get; }
        Vector3 Position { get; }
        float Radius { get; }
        IVehicleStorefront Storefront { get; }
    }

    public interface IFreeRoamSession
    {
        FreeRoamState State { get; }
        bool CanDrive { get; }
        string Status { get; }
        IWorldLocation ActiveLocation { get; }
        bool TryEnter(IWorldLocation location, out string failure);
        bool TryStartEvent(FreeRoamEventDefinition definition, out string failure);
        bool TryRecover(out string failure);
        bool TrySave(out string failure);
        bool TryLoad(out string failure);
        void ExitActivity();
        void TogglePause();
        void NavigateTo(Vector3 destination);
        IReadOnlyList<Vector3> NavigationRoute { get; }
    }
}
