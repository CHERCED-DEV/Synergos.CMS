using System;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubCancellationPolicyEvaluator"/> (seam
/// <see cref="ICancellationPolicyEvaluator"/>, política de cancelación del
/// vertical Hoteles): los 4 casos canónicos (ADR 0075) —
/// inválido (non-refundable→penalidad total) / happy (refundable a tiempo→0) /
/// filter (refundable tarde→penalidad) / idempotente (determinista).
/// </summary>
public class StubCancellationPolicyEvaluatorTests
{
    private static ICancellationPolicyEvaluator Make() => new StubCancellationPolicyEvaluator();

    private static readonly DateOnly CheckIn = new(2026, 7, 10);

    [Fact] // "inválido"/borde: non-refundable nunca reembolsa → penalidad total
    public void Evaluate_NonRefundable_AlwaysPenalized()
    {
        // Aun cancelando con mucha antelación, el non-refundable penaliza.
        var outcome = Make().Evaluate("STD-NREF", CheckIn, asOf: new DateOnly(2026, 6, 1));

        Assert.False(outcome.Refundable);
        Assert.True(outcome.PenaltyAmount > 0m);
    }

    [Fact] // happy: refundable con >1 día de antelación → sin penalidad
    public void Evaluate_RefundableInTime_NoPenalty()
    {
        var outcome = Make().Evaluate("STD-FLEX", CheckIn, asOf: new DateOnly(2026, 7, 5));

        Assert.True(outcome.Refundable);
        Assert.Equal(0m, outcome.PenaltyAmount);
    }

    [Fact] // filter: refundable cancelada tarde (día antes / mismo día) → penalidad de 1 noche
    public void Evaluate_RefundableLate_OneNightPenalty()
    {
        // freeUntil = checkIn - 1 = 2026-07-09; cancelar el 09 ya NO es "a tiempo".
        var outcome = Make().Evaluate("STD-FLEX", CheckIn, asOf: new DateOnly(2026, 7, 9));

        Assert.True(outcome.Refundable);
        Assert.True(outcome.PenaltyAmount > 0m);
    }

    [Fact] // idempotente/determinista: misma entrada → mismo resultado
    public void Evaluate_IsDeterministic()
    {
        var eval = Make();
        var asOf = new DateOnly(2026, 7, 8);

        var first = eval.Evaluate("DLX-FLEX", CheckIn, asOf);
        var second = eval.Evaluate("DLX-FLEX", CheckIn, asOf);

        Assert.Equal(first, second); // record equality
    }
}
