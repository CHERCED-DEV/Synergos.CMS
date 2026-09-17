using Microsoft.Extensions.Options;
using Synergos.Api.Pricing.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Pricing.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class PricingStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "pricing");
}

public interface IPriceStore
{
    Price? Find(string id);
    Price? FindBySubject(Ref subject);
    void Put(Price price);
}

public interface IPromotionStore
{
    Promotion? Find(string id);
    Promotion? FindByCode(string code);
    void Put(Promotion promotion);
}

public sealed class FileSystemPriceStore : IPriceStore
{
    private readonly JsonCollectionStore<Price> _store;

    public FileSystemPriceStore(IOptions<PricingStorageOptions> options)
        => _store = new JsonCollectionStore<Price>(options.Value.Root, "prices", p => p.Id);

    public Price? Find(string id) => _store.Find(id);

    public Price? FindBySubject(Ref subject)
    {
        var hit = _store.Where(p => p.Subject == subject);
        return hit.Count > 0 ? hit[0] : null;
    }

    public void Put(Price price) => _store.Put(price);
}

public sealed class FileSystemPromotionStore : IPromotionStore
{
    private readonly JsonCollectionStore<Promotion> _store;

    public FileSystemPromotionStore(IOptions<PricingStorageOptions> options)
        => _store = new JsonCollectionStore<Promotion>(options.Value.Root, "promotions", p => p.Id);

    public Promotion? Find(string id) => _store.Find(id);

    public Promotion? FindByCode(string code)
    {
        var hit = _store.Where(p => string.Equals(p.Code, code, StringComparison.OrdinalIgnoreCase));
        return hit.Count > 0 ? hit[0] : null;
    }

    public void Put(Promotion promotion) => _store.Put(promotion);
}
