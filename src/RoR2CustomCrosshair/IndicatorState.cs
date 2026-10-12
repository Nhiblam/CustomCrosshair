using System;
using RoR2;
using UnityEngine;

namespace RoR2CustomCrosshair;

/// <summary>Generic progress / charge / readiness value. Renderers know nothing about its source.</summary>
internal readonly struct ProgressIndicatorState : IEquatable<ProgressIndicatorState>
{
    // Progress changes are only rendered in steps this small.
    private const float Step = 1f / 512f;

    internal readonly bool Active;

    /// <summary>0 = just started / fully unavailable, 1 = complete / fully ready.</summary>
    internal readonly float Progress;

    internal readonly bool Ready;

    internal ProgressIndicatorState(float progress, bool ready)
    {
        Active = true;
        Ready = ready;
        Progress = ready ? 1f : Mathf.Round(Mathf.Clamp01(progress) / Step) * Step;
    }

    public bool Equals(ProgressIndicatorState other) =>
        Active == other.Active && Ready == other.Ready && Progress == other.Progress;
}

/// <summary>Generic ammo / stock / count value. Renderers know nothing about its source.</summary>
internal readonly struct CountIndicatorState : IEquatable<CountIndicatorState>
{
    internal readonly bool Active;
    internal readonly int Current;
    internal readonly int Maximum;

    internal CountIndicatorState(int current, int maximum)
    {
        Active = true;
        Current = Math.Max(0, current);
        Maximum = Math.Max(0, maximum);
    }

    public bool Equals(CountIndicatorState other) =>
        Active == other.Active && Current == other.Current && Maximum == other.Maximum;
}

/// <summary>
/// All indicator values for one frame, by channel. Channels give each
/// independent source (e.g. a weapon slot) a stable instance and layout
/// position. Reused every frame; no allocations.
/// </summary>
internal sealed class IndicatorFrame
{
    internal const int Channels = 2;

    internal readonly ProgressIndicatorState[] Progress = new ProgressIndicatorState[Channels];
    internal readonly CountIndicatorState[] Counts = new CountIndicatorState[Channels];

    internal void Clear()
    {
        Array.Clear(Progress, 0, Channels);
        Array.Clear(Counts, 0, Channels);
    }

    internal void SetProgress(int channel, ProgressIndicatorState state)
    {
        if ((uint)channel < Channels)
        {
            Progress[channel] = state;
        }
    }

    internal void SetCount(int channel, CountIndicatorState state)
    {
        if ((uint)channel < Channels)
        {
            Counts[channel] = state;
        }
    }

    /// <summary>True when any count channel is active.</summary>
    internal bool AnyCount => Counts[0].Active || Counts[1].Active;

    /// <summary>True when any progress channel is active.</summary>
    internal bool AnyProgress => Progress[0].Active || Progress[1].Active;

    internal void CopyTo(IndicatorFrame other)
    {
        Array.Copy(Progress, other.Progress, Channels);
        Array.Copy(Counts, other.Counts, Channels);
    }

    internal bool ContentEquals(IndicatorFrame other)
    {
        for (int i = 0; i < Channels; i++)
        {
            if (!Progress[i].Equals(other.Progress[i]) || !Counts[i].Equals(other.Counts[i]))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Supplies gameplay indicator values for one survivor. Must only read game
/// state. Writes into the frame's channels; leaves unused channels inactive.
/// </summary>
internal interface IIndicatorSource
{
    bool CanHandle(CharacterBody body);

    void Sample(CharacterBody body, IndicatorFrame frame);
}

/// <summary>Picks at most one indicator source per body (re-resolved when the body changes).</summary>
internal sealed class IndicatorSourceResolver
{
    private readonly IIndicatorSource[] _sources;
    private CharacterBody? _body;
    private IIndicatorSource? _source;

    internal IndicatorSourceResolver(params IIndicatorSource[] sources)
    {
        _sources = sources;
    }

    internal void Sample(CharacterBody body, IndicatorFrame frame)
    {
        frame.Clear();

        if (body != _body)
        {
            _body = body;
            _source = null;
            foreach (IIndicatorSource candidate in _sources)
            {
                if (candidate.CanHandle(body))
                {
                    _source = candidate;
                    break;
                }
            }
        }

        _source?.Sample(body, frame);
    }
}
