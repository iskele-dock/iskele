// Preferences belong to the widget's own expansion in Settings > Widget’lar.
namespace Iskele;
static class WidgetConfiguration
{
    static Settings S=>App.Settings;
    public static bool HasOptions(string id)=>id is "apps" or "clock" or "bigclock" or "flipclock" or "segclock" or "worldclock" or "worldtime" or "clockweather"
        or "media" or "mediacompact" or "mediamini" or "mediabar" or "weather" or "weatherdetails" or "forecast" or "hourly" or "sun" or "weatherstatus" or "today" or "week"
        or "toggles" or "screentime" or "todos" or "calendar" or "temperature" or "stocks" or "stockchart" or "crypto" or "currency"
        or "ailimits" or "claude" or "codex" or "gemini" or "opencode";
    public static FrameworkElement Build(string id)
    {
        var p=new StackPanel();
        void Add(FrameworkElement row){row.Margin=new Thickness(0,7,0,7);p.Children.Add(row);}
        void Toggle(string title,string description,bool value,Action<bool> set)=>Add(SettingsWindow.Row(title,description,
            SettingsWindow.Switch(value,false,i=>{set(i==1);SettingsWindow.Apply();})));
        void Text(string title,string description,string value,Action<string> set)=>Add(SettingsWindow.TextRow(title,description,value,set,160));
        if(id=="apps")return SettingsWindow.ApplicationOptions();
        if(id is "clock" or "bigclock" or "flipclock" or "segclock" or "worldclock" or "worldtime" or "clockweather")
        {
            Toggle("24 saat","Saat gösterimi",S.Clock24,v=>S.Clock24=v);
            Add(SettingsWindow.Row("Saate tıklayınca","Takvim ve bildirimler",Ui.Segmented(["İskele","Windows"],S.ClockClick=="windows"?1:0,i=>{S.ClockClick=i==1?"windows":"iskele";SettingsWindow.Apply();})));
            if(id=="worldtime")Text("Saat dilimi","Windows saat dilimi kimliği",S.WorldCity,v=>S.WorldCity=v);
        }
        if(id.StartsWith("media",StringComparison.Ordinal))
        {
            Toggle("Müzik yokken gizle","Duraklatılan parça görünür kalır",S.HideIdleMedia,v=>S.HideIdleMedia=v);
            Toggle("Kapaktan vurgu rengi","Çalan albümün rengini kullan",S.AccentFromArt,v=>S.AccentFromArt=v);
        }
        if(id is "weather" or "weatherdetails" or "forecast" or "hourly" or "sun" or "weatherstatus" or "clockweather" or "today" or "week")
            Text("Şehir","Hava durumu ve tahmin",S.WeatherCity,v=>S.WeatherCity=v);
        if(id=="toggles")Add(SettingsWindow.Row("Hızlı ayarlar","Wi-Fi ve Bluetooth yanındaki düğmeler",SettingsWindow.QuickTogglePicker()));
        if(id=="screentime")Toggle("Web sitelerini takip et","Yalnızca bu bilgisayarda",S.TrackSites,v=>S.TrackSites=v);
        if(id=="todos")Toggle("Alt+T","Hızlı yapılacak kutusu",S.AltTodo,v=>S.AltTodo=v);
        if(id=="calendar")
        {
            Text("iCal bağlantıları","Birden çok adresi boşlukla ayır",string.Join(" ",S.IcalUrls),v=>S.IcalUrls=v.Split(' ',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToList());
            Toggle("Toplantı öncesi müzik","Toplantıdan 2 dakika önce duraklat",S.PauseBeforeMeetings,v=>S.PauseBeforeMeetings=v);
        }
        if(id=="temperature")
        {
            Add(SettingsWindow.Row("Sıcaklık birimi","Sensör gösterimi",Ui.Segmented(["°C","°F"],S.TempUnit=="F"?1:0,i=>{S.TempUnit=i==1?"F":"C";SettingsWindow.Apply();})));
            Text("Sensör","Boş bırakılırsa otomatik seçilir",S.TempSensor,v=>S.TempSensor=v);
        }
        if(id is "stocks" or "stockchart")Text("Semboller","Virgülle ayır: AAPL, MSFT",string.Join(", ",S.Stocks),v=>S.Stocks=v.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(s=>s.ToUpperInvariant()).ToList());
        if(id=="crypto")Text("Kripto çiftleri","Virgülle ayır: BTCUSDT, ETHUSDT",string.Join(", ",S.Crypto),v=>S.Crypto=v.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(s=>s.ToUpperInvariant()).ToList());
        if(id=="currency")
        {
            Text("Kaynak para","Örn. USD",S.CurrencyFrom,v=>S.CurrencyFrom=v.ToUpperInvariant());
            Text("Hedef para","Örn. TRY",S.CurrencyTo,v=>S.CurrencyTo=v.ToUpperInvariant());
            Text("Miktar","Çevrilecek tutar",S.CurrencyAmount.ToString(Tr.C),v=>{if(double.TryParse(v,out var amount)&&double.IsFinite(amount)&&amount>=0)S.CurrencyAmount=amount;});
        }
        if(id is "ailimits" or "claude" or "codex" or "gemini" or "opencode")
        {
            Toggle("Limit uyarısı","Her sıfırlama döneminde bir uyarı",S.AiLimitAlerts,v=>S.AiLimitAlerts=v);
            Add(SettingsWindow.Row("Uyarı eşiği","Kullanım yüzdesi",Ui.Segmented(["%80","%90","%95"],S.AiAlertPercent==80?0:S.AiAlertPercent==95?2:1,i=>{S.AiAlertPercent=new[]{80,90,95}[i];SettingsWindow.Apply();})));
        }
        if(p.Children.Count==0)
        {
            var text=Ui.Text("Bu widget genel tema ve boyut ayarlarını kullanır.",12,"FgDim");
            text.TextWrapping=TextWrapping.Wrap;Add(text);
        }
        return p;
    }
}
