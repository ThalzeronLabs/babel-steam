using Babel.Core;
using Xunit;

namespace Babel.Tests;

/// <summary>The rules the legal and security reviews asked for: which pages are touched, where Babel connects, the
/// global budget, and who may own the port.</summary>
public class GuardTests
{
    const int Port = 9333;
    const string Ws = "ws://127.0.0.1:9333/devtools/page/X";

    static Verdict J(string title, string url, bool chat = false, string? ws = Ws, string type = "page") =>
        Pages.Judge(new Target("id", type, title, url, ws), Port, chat);

    [Theory]
    [InlineData("https://store.steampowered.com/news/app/570")]
    [InlineData("https://store.steampowered.com/app/570/")]
    [InlineData("https://steamcommunity.com/app/570/reviews/")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=1")]
    [InlineData("https://steamcommunity.com/app/570/discussions/0/1/")]
    public void Public_store_and_community_pages_are_translated(string url) => Assert.Equal(Verdict.Translate, J("x", url));

    [Theory]
    [InlineData("https://store.steampowered.com/account/")]
    [InlineData("https://store.steampowered.com/account/history/")]
    [InlineData("https://store.steampowered.com/cart/")]
    [InlineData("https://store.steampowered.com/checkout/?purchasetype=self")]
    [InlineData("https://store.steampowered.com/steamaccount/addfunds")]
    [InlineData("https://store.steampowered.com/digitalgiftcards/")]
    [InlineData("https://store.steampowered.com/login/")]
    [InlineData("https://steamcommunity.com/login/home/")]
    [InlineData("https://steamcommunity.com/chat/")]
    [InlineData("https://steamcommunity.com/tradeoffer/new/")]
    [InlineData("https://steamcommunity.com/market/")]
    [InlineData("https://steamcommunity.com/id/someone/inventory/")]
    [InlineData("https://steamcommunity.com/id/someone/edit/info")]
    public void Account_wallet_payment_login_and_trading_pages_are_never_translated(string url) =>
        Assert.Equal(Verdict.Sensitive, J("x", url));

    [Theory]
    [InlineData("https://login.steampowered.com/")]
    [InlineData("https://checkout.steampowered.com/checkout/")]
    [InlineData("https://help.steampowered.com/wizard/")]
    [InlineData("https://example.com/")]
    public void Other_sites_are_never_translated(string url) => Assert.Equal(Verdict.UnknownSite, J("x", url));

    [Fact]
    public void The_main_client_window_is_translated_and_its_menus_are_not()
    {
        Assert.Equal(Verdict.Translate, J("Steam", "about:blank?browserType=4"));
        Assert.Equal(Verdict.ClientMenu, J("Library Supernav", "about:blank?createflags=1"));
        Assert.Equal(Verdict.ClientMenu, J("Friends Root Menu", "about:blank?createflags=1"));
    }

    [Fact]
    public void Chat_windows_are_off_by_default_and_follow_the_setting()
    {
        Assert.False(new Settings().TranslateChat);
        Assert.Equal(Verdict.Chat, J("A friend", "about:blank?createflags=1"));
        Assert.Equal(Verdict.Translate, J("A friend", "about:blank?createflags=1", chat: true));
    }

    [Theory]
    [InlineData("Sign in to Steam")]
    [InlineData("Entrar no Steam")]
    [InlineData("Steam Settings")]
    [InlineData("Configurações da Steam")]
    [InlineData("Friends List")]
    public void Private_client_windows_stay_closed_even_with_the_chat_on(string title) =>
        Assert.Equal(Verdict.Sensitive, J(title, "about:blank?createflags=1", chat: true));

    [Theory]
    [InlineData("ws://192.168.0.10:9333/devtools/page/X")]
    [InlineData("ws://evil.example:9333/devtools/page/X")]
    [InlineData("ws://127.0.0.1:9444/devtools/page/X")]
    [InlineData("wss://127.0.0.1:9333/devtools/page/X")]
    public void Babel_only_connects_to_this_PC_on_the_configured_port(string ws) =>
        Assert.Equal(Verdict.NotLocal, J("Steam", "about:blank", ws: ws));

    [Fact]
    public void Hidden_contexts_workers_and_tools_are_never_touched()
    {
        Assert.Equal(Verdict.HiddenContext, J("SharedJSContext", "https://steamloopback.host/index.html"));
        Assert.Equal(Verdict.HiddenContext, J("DevTools", "devtools://devtools/bundled"));
        Assert.Equal(Verdict.NotAPage, J("Worker", "https://store.steampowered.com/", type: "service_worker"));
        Assert.Equal(Verdict.NotAPage, J("Store", "https://store.steampowered.com/", ws: null));
    }

    [Fact]
    public void Only_five_DevTools_commands_exist_and_none_touches_cookies_storage_network_or_input()
    {
        Assert.Equal(5, Pages.AllowedCommands.Count);
        Assert.DoesNotContain(Pages.AllowedCommands, c => c.StartsWith("Network.") || c.StartsWith("Storage.")
            || c.StartsWith("Input.") || c.StartsWith("Fetch.") || c.StartsWith("DOMStorage.") || c.Contains("Cookie"));
    }

    [Fact]
    public void Reopening_Steam_is_off_by_default() => Assert.False(new Settings().ReopenSteam);

    [Fact]
    public void The_budget_stops_at_the_minute_limit_and_resets()
    {
        var t = new DateTime(2026, 11, 14, 12, 0, 0);
        var b = new Budget(perMinute: 100, perDay: 1000, clock: () => t);
        Assert.True(b.TryTake(60));
        Assert.False(b.TryTake(50));
        Assert.True(b.LimitReached);
        t = t.AddMinutes(1);
        Assert.True(b.TryTake(50));
        Assert.False(b.LimitReached);
    }

    [Fact]
    public void The_budget_stops_at_the_day_limit_until_tomorrow()
    {
        var t = new DateTime(2026, 11, 14, 22, 0, 0);
        var b = new Budget(perMinute: 1000, perDay: 1500, clock: () => t);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(b.TryTake(500));
            t = t.AddMinutes(2);
        }
        Assert.False(b.TryTake(1));
        t = new DateTime(2026, 11, 15, 0, 1, 0);
        Assert.True(b.TryTake(500));
    }

    [Theory]
    [InlineData(@"C:\Program Files (x86)\Steam\bin\cef\cef.win7x64\steamwebhelper.exe", true)]
    [InlineData(@"C:\Program Files (x86)\Steam\steamwebhelper.exe", true)]
    [InlineData(@"C:\Users\x\AppData\Local\Temp\steamwebhelper.exe", false)]
    [InlineData(@"C:\Program Files (x86)\Steam\bin\other.exe", false)]
    [InlineData(@"C:\Program Files (x86)\SteamFake\steamwebhelper.exe", false)]
    [InlineData(null, false)]
    public void Only_the_web_helper_inside_Steams_folder_owns_the_port(string? path, bool ok) =>
        Assert.Equal(ok, PortOwner.IsHelperOf(path, @"C:\Program Files (x86)\Steam"));
}
