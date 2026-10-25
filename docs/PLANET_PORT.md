# LADİR web → Unity aktarımı

25 Ekim 2026. Web v1.26.1 / commit `34841d6abc1c1c12251c2ace18ffb853895db16f`; Unity 6000.0.80f1 / URP 17.

Küresel Planet Studio sahnesi Windows Player olarak derlenip çalıştırıldı. Önceki disk/import engeli bu turda yaşanmadı. Açık editör ve kullanıcı sahneleri kapatılmadı. Gerçek ölçümler ve ekran görüntüleri [NATIVE_VALIDATION.md](NATIVE_VALIDATION.md) içinde.

## Dünya ve kaynak eşliği

Varsayılan dünya özgün `river-v42.planet` dosyasıdır: **4.000.000 m yarıçap / 8.000 km çap**. Dağ/vadi yükseklikleri, stamp verileri, nehir ve göl kotları korunur. Fizik double hassasiyetinde metre kullanır. Yalnız render sınırında X yansıtılır ve kayan başlangıç noktası çıkarılır.

- Paket SHA-256: `07e3a31e5068b59f6208fc880c1a6e2f0455b595e9475c833372625ecb2c1e3f`.
- Kanonik dünya imzası: `b7aefd351cef8c3f6e23e5878c5369987fa41d29e28a4fe9297c891ad09efada`.
- Altı dünya türündeki 2.118 örnek eşleşir; en büyük yükseklik farkı 6,96e-11 m.
- Sekiz tile içindeki 2.439 ağaçta tür, kök, boy, dönüş ve yön eşleşir.
- 1.534 uçuş/kayıt/navigasyon kontrolü, 1.089 drenaj düğümü ve 216 doğrudan DFT dalga örneği geçti. Bunlar bağımsız oyun senaryosu sayıları değildir.
- C# kaydı özgün web okuyucusunda aynı coğrafya imzasıyla açıldı. Mevcut üç Node testi geçti.

Yeni seed üretimi, havza ve bölge bağları için özgün JS üreticisi yerel Node 24 işlemiyle korunmuştur. Hazır dünyayı oynamak Node veya tarayıcı gerektirmez. Yeni seed oluşturma henüz tamamen C# değildir.

Kayıt geçici dosya ve atomik değiştirme kullanır. Şema, üretici sürümü ve build uyuşmazlığı reddedilir. Yanında `.sha256` varsa bütün paket doğrulanır. Yan dosyası olmayan dış paketlerin gömülü kanonik hash'i C# içinde henüz yeniden hesaplanmaz.

## Bu turda değişenler

| Alan | Uygulama |
| --- | --- |
| Sahne / build | Mineral mip yükleme hatası giderildi; sahne ve URP varlıkları depoya eklendi; Windows build doğrulandı. |
| Arazi | Nehir/kıyı genişliğine duyarlı LOD, webin yörünge piksel izi kuralı, farklı LOD kenarlarını gösterilen kaba üçgene bağlama, görev kuyruğunda tekrarlı doğrusal aramayı kaldırma. |
| Su | Özgün 64×64 FFT spektrumu; compute shader ve CPU yedeği; kayan başlangıç noktasında sabit dalga fazı; GPU sonucu sayısal olarak test edilir. |
| Atmosfer / bulut | Optik derinlik LUT, yarı çözünürlüklü raymarch, beş fazlı temporal geçmiş, derinlik/kamera/ışık reddi. İnce siluetlerde siyah delik oluşturan bileştirme düzeltildi. |
| Arayüz | Soldaki editör, JSON ayar aç/kaydet, dalga/rüzgâr/hacim kontrolleri, geçit ve boğaz rotaları, 32×32 km havza incelemesi. |
| Efekt | Warp tüneli interpolasyonla çizilen gemiye bağlandı; fizik adımı nedeniyle kilometrelerce öne kaçması giderildi. Global pozlama ve kaynak temizliği eklendi. |
| Test | Editör, uçuş, yoğun bulut/çatışma, boost bırakma, 1.000 km uzaya warp, havza ekranı, GPU FFT ve LOD kenar ölçümü. |

Havza aracı son arazi yüzeyini teşhis eder; araziyi değiştirmez ve eksik nehir üretmiş sayılmaz. 1.089 düğümlük özgün referansın havzaları ve akış alıcıları eşleşti. Yerel yüksek çözünürlüklü görünüm 125 m/hücre kullanır.

## Kod

- `Assets/Ladir/Planet/Kernel`: dünya, stamp, hidroloji, biyom, ağaç, uçuş, navigasyon, FFT ve drenaj.
- `PlanetTerrain.cs`: küresel quadtree, iptal, parent LOD, kenar bağlantısı, upload, varış ön yükleme.
- `PlanetStudio.cs`, `PlanetHUD.cs`, `Ladir.uss`: kamera, giriş, kayıt, üretim ve yerel arayüz.
- `PlanetOcean.cs`, `Shaders/Ocean.compute`: GPU dalga hesabı ve CPU yedeği.
- `PlanetClouds.cs`, `CloudRendererFeature.cs`, `AtmosphereCommon.hlsl`: atmosfer/bulut/temporal geçmiş.
- `PlanetReferenceInspector.cs`, `PlanetRoutePreview.cs`: havza ve rota araçları.
- `PlanetEffects.cs`: thruster, ışık, ses, parçacık, boost ve warp.
- `PlanetPortSmoke.cs`: yalnız açık test bayrağıyla çalışan oyuncu testi.
- `Generated/Planet`, `Scenes/PlanetStudio.unity`: doğrulanan bağlı sahne varlıkları.

## Tam eşdeğerlik için kalanlar

1. Aynı ışık ve kayıtlı kamerayla piksel düzeyinde web/Unity kıyası tamamlanmadı. URP materyalleri, kıyılar, yörünge kar maskesi, gölgeler ve bulut ışığında görsel farklar var. Nehir çevresindeki tırtıklar yalnız LOD açıklığı değildir; su ve materyal örneklemesi ayrıca incelenmeli.
2. Küresel çizici CPU mesh + GPU vertex upload kullanır. Ortak GPU grid/displacement ve önceki bölgesel bake okuyucusu bu sahneye henüz bağlanmadı. Eski 32×32 km sahnesinin paket/Jobs yolu korunur.
3. Farklı LOD kenarları bağlanır; sürekli zaman tabanlı geomorph henüz yok. Tüm cube kenarları, köşeler, su birleşimleri ve uzun uçuş için daha geniş rota testi gerekir. Ölçülen kenar sınırı yalnız kayıtlı rotadaki örnekleri kapsar.
4. Yeni seed üretiminin C#'a taşınması, yan dosyasız kanonik hash denetimi, dünya ölçeğinde kalıcı cache, uzun uçuşta düğüm meta verisini budama ve sürücü düzeyinde VRAM ölçümü kalan işlerdir.
5. Sabit 100 FPS sağlanmadı. Hacimsel efekt ve arazi GPU maliyeti önemlidir; ilk detay geçişi, upload ve kenar düzenleme de uzun kare üretebilir. Hızlı uzay warp'ı, yoğun alçak uçuş performansı yerine kullanılamaz.

GitHub aktarımı taslak dalda tutulur. Çalışan oyuncu ve gerçek test sonuçları vardır; tam görsel/mimari eşdeğerlik iddiası yoktur.
