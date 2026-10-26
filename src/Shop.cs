using LmCode;
using LmCode.Stdlib;

namespace Sample;

// Ordinary managed objects are the domain. The VM sees typed ObjRef handles,
// never pointers or serialized copies; Context owns them for this execution.
sealed class Money(long cents) { public long Cents = cents; }
sealed class Items { public readonly List<ObjRef> Values = []; }
sealed class Shop { public readonly Items Items = new(); }
sealed class Ledger
{
    public long Balance, Entries;
    public VmString Note = VmString.FromString("");
}

sealed class Host
{
    public readonly Registry Registry = new(); // Default string/math/collection/file catalog.
    public readonly Context Context = new();
    public readonly Shop Shop = new();
    public readonly Ledger Ledger = new();

    private Value Money(long cents) => Value.Object("Money", Context.Insert("Money", new Money(cents)));
    private static Value Text(string text) => Value.Str(VmString.FromString(text));
    private Value Units(double units)
    {
        // Match C++ round: halfway away from zero, not C#'s default banker's rounding.
        double cents = Math.Round(units * 100, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(cents) || cents < -9223372036854775808.0 || cents >= 9223372036854775808.0)
            throw Error.Host("money: amount out of range");
        return Money((long)cents);
    }

    internal static Money GetMoney(Context context, ObjRef reference)
    {
        if (reference.Index < 0) throw Error.Host("money: null handle");
        return context.Get<Money>(reference);
    }

    public Host()
    {
        Registry.Context("shop", "Shop").Context("ledger", "Ledger");
        Context.SetObject("shop", "Shop", Shop);
        Context.SetObject("ledger", "Ledger", Ledger);
        ObjRef items = Context.Insert("Items", Shop.Items);
        foreach (long cents in new long[] { 125, 250, 375 }) Shop.Items.Values.Add(Money(cents).AsObj());
        TestingCatalog.RegisterTesting(Registry); // Explicit opt-in: raw-byte print and assert_close.
        Conversions.Register(Registry);

        // A parameter tuple identifies an overload; int prices are cents, floats are units.
        Registry.Function("money", ["int"], "Money", (_, a) => Money(a[0].AsInt()));
        Registry.Function("money", ["float"], "Money", (_, a) => Units(a[0].AsFloat()));
        Registry.Function("fee", ["int"], "int", (_, a) => Value.Int(unchecked(a[0].AsInt() + 5)));
        Registry.Function("sample_tmp", [], "string", (_, _) =>
        {
            string? path = Environment.GetEnvironmentVariable("SAMPLE_TMP");
            if (string.IsNullOrEmpty(path)) throw Error.Host("SAMPLE_TMP is required");
            return Text(path);
        });

        // Method callbacks receive Context, receiver handle and ordered tagged arguments.
        Registry.Method("Money", "amount", [], "int", (c, self, _) => Value.Int(GetMoney(c, self).Cents));
        Registry.Method("Money", "currency", [], "Currency", (c, self, _) => { GetMoney(c, self); return Text("USD"); }); // String-backed enum.
        Registry.Method("Money", "plus", ["Money"], "Money", (c, self, a) => Money(unchecked(GetMoney(c, self).Cents + GetMoney(c, a[0].AsObj()).Cents)));
        Registry.Method("Money", "minus", ["Money"], "Money", (c, self, a) => Money(unchecked(GetMoney(c, self).Cents - GetMoney(c, a[0].AsObj()).Cents)));
        Registry.Method("Money", "times", ["int"], "Money", (c, self, a) => Money(unchecked(GetMoney(c, self).Cents * a[0].AsInt())));
        Registry.Method("Money", "neg", [], "Money", (c, self, _) => Money(unchecked(-GetMoney(c, self).Cents)));
        Registry.Method("Money", "set", ["int"], "Money", (c, self, a) =>
        {
            GetMoney(c, self).Cents = a[0].AsInt();
            return Value.Object("Money", self); // returns:self registers the concrete owner type.
        });

        // len/at make a domain collection iterable; foreach snapshots its length once.
        Registry.Method("Items", "len", [], "int", (c, self, _) => Value.Int(c.Get<Items>(self).Values.Count));
        Registry.Method("Items", "at", ["int"], "Money", (c, self, a) =>
        {
            var values = c.Get<Items>(self).Values;
            long index = a[0].AsInt();
            if (index < 0 || index >= values.Count) throw Error.Host("items: index out of range");
            return Value.Object("Money", values[(int)index]);
        });
        Registry.Method("Shop", "items", [], "Items", (_, _, _) => Value.Object("Items", items));
        Registry.Method("Shop", "total", [], "Money", (c, self, _) =>
        {
            long total = 0;
            foreach (ObjRef item in c.Get<Shop>(self).Items.Values) total = unchecked(total + c.Get<Money>(item).Cents);
            return Money(total);
        });
        Registry.Method("Shop", "price", ["int"], "Money", (_, _, a) => Money(a[0].AsInt()));
        Registry.Method("Shop", "price", ["float"], "Money", (_, _, a) => Units(a[0].AsFloat()));
        Registry.Method("Shop", "currency", [], "Currency", (_, _, _) => Text("USD"));
        Registry.Method("Shop", "label", [], "string", (_, _, _) => Text("sample shop"));

        Registry.Method("Ledger", "record", ["Money"], "Ledger", (c, self, a) =>
        {
            var ledger = c.Get<Ledger>(self);
            ledger.Balance = unchecked(ledger.Balance + GetMoney(c, a[0].AsObj()).Cents);
            ledger.Entries = unchecked(ledger.Entries + 1);
            return Value.Object("Ledger", self);
        });
        Registry.Method("Ledger", "balance", [], "Money", (c, self, _) => Money(c.Get<Ledger>(self).Balance));
        Registry.Method("Ledger", "count", [], "int", (c, self, _) => Value.Int(c.Get<Ledger>(self).Entries));
        Registry.Method("Ledger", "note", ["string"], "void", (c, self, a) =>
        {
            c.Get<Ledger>(self).Note = a[0].AsStr(); // VmString preserves NUL and invalid UTF-8.
            return Value.None; // void pushes no VM result.
        });
        Registry.Method("Ledger", "note", [], "string", (c, self, _) => Value.Str(c.Get<Ledger>(self).Note));
        Registry.Method("Ledger", "reject", ["string"], "void", (_, _, a) => throw Error.Host(a[0].AsStr().ToString()));
    }
}
