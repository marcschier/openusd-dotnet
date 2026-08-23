// Copyright (c) marcschier. Licensed under the MIT License.

using Avalonia.Input;

namespace OpenUsd.Viewer.Tests;

/// <summary>
/// Behavioural coverage of the discrete physics shortcut repeat guard and of the command target
/// resolution the interaction controls run through.
/// </summary>
/// <remarks>
/// Both are pure state machines on purpose. The window only forwards keys and selections into
/// them, so asserting them directly proves the behaviour the operator sees without needing a
/// rendered window, a stage, or a solver.
/// </remarks>
public sealed class ViewerPhysicsShortcutRepeatGuardTests
{
    private static readonly Key[] Discrete =
    [
        Key.K, Key.J, Key.N, Key.B, Key.Q, Key.G,
        Key.E, Key.R, Key.H, Key.X, Key.Z, Key.Y,
    ];

    [Test]
    public async Task EveryDiscretePhysicsShortcutRunsOnceWhileTheKeyIsHeld()
    {
        foreach (Key key in Discrete)
        {
            var guard = new ViewerPhysicsShortcutRepeatGuard();
            await Assert.That(ViewerPhysicsShortcutRepeatGuard.IsGuarded(key)).IsTrue();

            // The first press is the command; every repeat the operating system sends while the
            // key stays down must be refused, or a held Z unwinds the whole undo history.
            await Assert.That(guard.TryPress(key)).IsTrue();
            for (int repeat = 0; repeat < 12; repeat++)
            {
                await Assert.That(guard.TryPress(key)).IsFalse();
            }

            guard.Release(key);
            await Assert.That(guard.TryPress(key)).IsTrue();
        }
    }

    [Test]
    public async Task EveryShortcutKeyIsGuardedIndependently()
    {
        var guard = new ViewerPhysicsShortcutRepeatGuard();
        foreach (Key key in Discrete)
        {
            await Assert.That(guard.TryPress(key)).IsTrue();
        }

        // Twelve keys need twelve independent bits: a guard that shared one would let a second
        // held key through, or refuse the first press of the next key.
        foreach (Key key in Discrete)
        {
            await Assert.That(guard.TryPress(key)).IsFalse();
        }

        guard.Release(Key.Z);
        await Assert.That(guard.TryPress(Key.Z)).IsTrue();
        await Assert.That(guard.TryPress(Key.Y)).IsFalse();
    }

    [Test]
    public async Task TheMovementKeysAreNotGuardedBecauseMovementIsHeldRatherThanCommanded()
    {
        var guard = new ViewerPhysicsShortcutRepeatGuard();
        foreach (Key key in new[] { Key.W, Key.A, Key.S, Key.D, Key.Space, Key.C })
        {
            await Assert.That(ViewerPhysicsShortcutRepeatGuard.IsGuarded(key)).IsFalse();
            await Assert.That(guard.TryPress(key)).IsTrue();
            await Assert.That(guard.TryPress(key)).IsTrue();
        }
    }

    [Test]
    public async Task AFocusTransferAndADeactivationBothDropEveryHeldShortcutKey()
    {
        var guard = new ViewerPhysicsShortcutRepeatGuard();
        await Assert.That(guard.TryPress(Key.N)).IsTrue();
        await Assert.That(guard.TryPress(Key.Z)).IsTrue();

        // Focus moved into the native child, so the releases never arrive. Without the reset the
        // next press of the same key is mistaken for a repeat and the command never runs again.
        guard.ResetForFocusTransfer();
        await Assert.That(guard.TryPress(Key.N)).IsTrue();
        await Assert.That(guard.TryPress(Key.Z)).IsTrue();

        guard.Reset();
        await Assert.That(guard.TryPress(Key.N)).IsTrue();
    }

    [Test]
    public async Task ReleasingAKeyThatWasNeverPressedChangesNothing()
    {
        var guard = new ViewerPhysicsShortcutRepeatGuard();
        guard.Release(Key.Z);
        guard.Release(Key.F);
        await Assert.That(guard.TryPress(Key.Z)).IsTrue();
        await Assert.That(guard.TryPress(Key.Z)).IsFalse();
    }
}
