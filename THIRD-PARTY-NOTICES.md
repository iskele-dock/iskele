# Üçüncü taraf bileşenler

Iskele aşağıdaki bileşenleri içerir ya da kullanır. Her birinin lisansı kendi sahibine aittir.

## Windows Liquid Glass (HLSL cam görüntüleyici)

- Kaynak: https://github.com/PedroRuedas/Windows-Liquid-glass (revizyon `cc657382355c4398acf9a9a975fdad836ecd5639`)
- Lisans: GNU GPL v3 — tam metin [`vendor/LiquidGlass/LICENSE`](vendor/LiquidGlass/LICENSE)
- Konum: `vendor/LiquidGlass/`. Iskele için yapılan değişiklikler [`vendor/LiquidGlass/NOTICE.md`](vendor/LiquidGlass/NOTICE.md) dosyasında listelenmiş ve kaynakta `[Iskele]` ile işaretlenmiştir.
- Bu bileşen GPL v3 olduğu için Iskele'nin tamamı da GPL v3 ile dağıtılır.

## Sağlayıcı logoları (AI widget'ları)

- Claude, OpenAI/Codex, Gemini ve OpenCode işaretleri: [`src/Assets/Providers/NOTICE.md`](src/Assets/Providers/NOTICE.md)
- İkon kütüphanesi lisansları: `src/Assets/Providers/lobe-icons-LICENSE.txt`, `src/Assets/Providers/models-dev-LICENSE.txt`
- Markalar sahiplerine aittir; yalnızca ilgili kullanım widget'ını tanımlamak için kullanılır.

## NuGet paketleri

| Paket | Lisans |
|---|---|
| System.Management | MIT (.NET Foundation) |
| Vortice.Direct3D11 ve bağımlılıkları (Vortice.DXGI, Vortice.DirectX, Vortice.Mathematics, SharpGen.Runtime) | MIT |
| Microsoft.Windows.SDK.NET / WinRT.Runtime (.NET Windows hedefi ile gelir) | MIT |

## Çevrimiçi veri kaynakları

Widget'lar şu herkese açık servislerden veri çeker; kullanım koşulları ilgili servislere aittir: Open‑Meteo, open.er-api.com, Binance, Yahoo Finance, GitHub.
