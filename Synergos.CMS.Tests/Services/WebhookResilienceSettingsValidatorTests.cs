using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="WebhookResilienceSettingsValidator"/> (Ola 161,
/// ADR 0070). El validator nunca retorna Failure — siempre Success +
/// warning para typos (no fatal).
/// </summary>
public class WebhookResilienceSettingsValidatorTests
{
    [Fact]
    public void Validate_EmptyPerChannel_ReturnsSuccess()
    {
        var validator = new WebhookResilienceSettingsValidator(NullLogger<WebhookResilienceSettingsValidator>.Instance);
        var settings = new WebhookResilienceSettings();

        var result = validator.Validate(name: null, settings);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_KnownFactoryName_ReturnsSuccess()
    {
        var validator = new WebhookResilienceSettingsValidator(NullLogger<WebhookResilienceSettingsValidator>.Instance);
        var settings = new WebhookResilienceSettings
        {
            PerChannel = new Dictionary<string, WebhookResilienceChannelOverride>
            {
                ["comment-moderation-teams"] = new() { AttemptTimeoutSeconds = 30 },
            },
        };

        var result = validator.Validate(name: null, settings);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_UnknownFactoryName_NonStrict_StillReturnsSuccess()
    {
        // Default mode (StrictValidation=false): typos solo loguean warning.
        var validator = new WebhookResilienceSettingsValidator(NullLogger<WebhookResilienceSettingsValidator>.Instance);
        var settings = new WebhookResilienceSettings
        {
            PerChannel = new Dictionary<string, WebhookResilienceChannelOverride>
            {
                ["typo-channel-name"] = new() { MaxRetryAttempts = 5 },
            },
        };

        var result = validator.Validate(name: null, settings);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Fact]
    public void Validate_UnknownFactoryName_StrictMode_ReturnsFailure()
    {
        // Ola 194 — StrictValidation=true falla boot con typo.
        var validator = new WebhookResilienceSettingsValidator(NullLogger<WebhookResilienceSettingsValidator>.Instance);
        var settings = new WebhookResilienceSettings
        {
            StrictValidation = true,
            PerChannel = new Dictionary<string, WebhookResilienceChannelOverride>
            {
                ["typo-channel-name"] = new() { MaxRetryAttempts = 5 },
            },
        };

        var result = validator.Validate(name: null, settings);

        Assert.True(result.Failed);
        Assert.NotNull(result.FailureMessage);
        Assert.Contains("typo-channel-name", result.FailureMessage);
    }

    [Fact]
    public void Validate_KnownFactoryName_StrictMode_StillSucceeds()
    {
        var validator = new WebhookResilienceSettingsValidator(NullLogger<WebhookResilienceSettingsValidator>.Instance);
        var settings = new WebhookResilienceSettings
        {
            StrictValidation = true,
            PerChannel = new Dictionary<string, WebhookResilienceChannelOverride>
            {
                ["comment-moderation-teams"] = new() { MaxRetryAttempts = 5 },
            },
        };

        var result = validator.Validate(name: null, settings);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }

    [Theory]
    [InlineData("comment-moderation-webhook")]
    [InlineData("comment-moderation-slack")]
    [InlineData("comment-moderation-discord")]
    [InlineData("comment-moderation-teams")]
    [InlineData("form-submission-webhook")]
    [InlineData("form-submission-slack")]
    [InlineData("form-submission-discord")]
    [InlineData("form-submission-teams")]
    [InlineData("cart-abandonment-webhook")]
    [InlineData("cart-abandonment-slack")]
    [InlineData("cart-abandonment-discord")]
    [InlineData("cart-abandonment-teams")]
    public void Validate_AllTwelveKnownFactoryNames_NoIssues(string knownName)
    {
        var validator = new WebhookResilienceSettingsValidator(NullLogger<WebhookResilienceSettingsValidator>.Instance);
        var settings = new WebhookResilienceSettings
        {
            PerChannel = new Dictionary<string, WebhookResilienceChannelOverride>
            {
                [knownName] = new() { MaxRetryAttempts = 5 },
            },
        };

        var result = validator.Validate(name: null, settings);

        Assert.Equal(ValidateOptionsResult.Success, result);
    }
}
