// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Regression test for https://github.com/dotnet/runtime/issues/74020
// The JIT should fold (u2 / C1) / C2 into u2 / (C1 * C2) to avoid
// emitting an unnecessary shr instruction.

using System.Runtime.CompilerServices;
using Xunit;

public class Runtime_74020
{
    // These two methods should produce identical results for all inputs.
    // The JIT should fold the two divisions in F into a single division
    // matching G.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static uint F(uint u2) => u2 / 2939745 / 4;

    [MethodImpl(MethodImplOptions.NoInlining)]
    static uint G(uint u2) => u2 / 11758980;

    [Fact]
    public static int TestEntryPoint()
    {
        uint[] testValues = { 0, 1, 2939744, 2939745, 2939746, 11758979, 11758980, 11758981,
                              100000000, 500000000, uint.MaxValue - 1, uint.MaxValue };

        foreach (uint v in testValues)
        {
            uint f = F(v);
            uint g = G(v);
            if (f != g)
            {
                return 101;
            }
        }

        return 100;
    }
}
