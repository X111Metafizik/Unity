# LADİR web → Unity aktarımı

25 Ekim 2026. Kaynak web sürümü v1.26.1, commit `34841d6abc1c1c12251c2ace18ffb853895db16f`.

Bu değişiklik yalnız bir tasarım raporu değildir: C# arazi çekirdeği, küresel sahne akışı, uçuş, UI Toolkit arayüzü ve URP efekt kaynakları eklendi. Ancak yeni sahnenin Unity içinde görüntülenmesi henüz doğrulanamadı. Tamamlanmış birebir görsel aktarım veya 100 FPS iddiası yoktur.

## Dünya verisi

Varsayılan dünya özgün `river-v42.planet` dosyasıdır. Yarıçap 4.000.000 metre, çap 8.000 km. Ana referans bölge koordinatları ve bütün gezegenin yön vektörleri korunur. Render sınırında yalnız X ekseni yansıtılır; kayıtlardaki fiziksel koordinatlar değiştirilmez. Dağlar, vadiler, nehirler, göller, kraterler ve stamp'lar özgün veriden örneklenir.

Dosya SHA-256: `07e3a31e5068b59f6208fc880c1a6e2f0455b595e9475c833372625ecb2c1e3f`.

Kanonik dünya imzası: `b7aefd351cef8c3f6e23e5878c5369987fa41d29e28a4fe9297c891ad09efada`.

Yeni dünya üretimi, havza oluşturma ve bölge bağları için özgün JS üreticisi yerel Node işlemiyle korunmuştur. Bu, C# oyun döngüsünün JavaScript çalıştırdığı anlamına gelmez. Yeni seed oluşturmak için Node 24 gerekir; hazır dünyayı oynamak için gerekmez. Kaydetme geçici dosya ve atomik değiştirme kullanır. Paket şeması, generator sürümü/build kimliği ve yarıçap uyuşmazlıkları reddedilir. Yanındaki `.sha256` dosyası varsa bütün paket denetlenir. Haricî, yan dosyası olmayan paketlerde gömülü kanonik hash henüz C# içinde yeniden hesaplanmaz; bu bir kalan doğrulama işidir.

## Kodun dağılımı

| Yol | Görev |
| --- | --- |
| `Assets/Ladir/Planet/Kernel` | Saf C# dünya okuyucu, stamp, arazi, biyom, hidroloji, ağaç yerleşimi, geçit doğrulama, uçuş fiziği |
| `PlanetTerrain.cs` | Küresel quadtree, iptal edilebilir arka plan üretimi, parent LOD, ufuk elemesi, GPU mesh gönderimi, warp varış ön yüklemesi |
| `PlanetStudio.cs` | Sahne, koordinat dönüşümü, kamera, kullanıcı girişleri, dünya aç/kaydet/üret |
| `PlanetHUD.cs`, `Ladir.uss` | Editör, görünüm/inceleme, keşif işaretleri, seçili yüzey, uçuş HUD ve yetenekler |
| `PlanetVegetation.cs` | Özgün türler, instancing, 96 tile bitki önbelleği, gösterilen üçgene oturan kökler |
| `PlanetEffects.cs` | Thruster, ışıklar, çizgi/parçacık havuzları, ses, boost ve warp görüntüsü |
| `PlanetClouds.cs`, `CloudRendererFeature.cs` | Özgün hacim gürültüsü, yarı çözünürlük raymarch, derinlik bileştirme, yoğun bulutta şimşek |
| `PlanetHydrologyOverlay.cs`, `PlanetRoutePreview.cs` | Kaydedilmiş akarsu/göl işaretleri ve doğrulanan geçit animasyonu |
| `Assets/Ladir/Shaders` | URP arazi, küresel su, gökyüzü, bitki, bulut, warp ve çizgi shader'ları |
| `Assets/Ladir/Editor/PlanetSetup.cs` | Yeni sahne ve materyal/pipeline bağlantıları; Windows build komutu |
| `Assets/StreamingAssets/Ladir/authoring` | Özgün JS üreticisi ve kaynak imzaları |
| `tools`, `evidence/planet-port` | Tekrarlanabilir eşlik testleri, dışa aktarıcılar, gerçek sonuçlar |

## Yapılan doğrulama

- 2.118 arazi örneği: Terran, alien, desert, Mars, Moon ve volcanic; yüzler, kenarlar, nehir/göl ve referans bölge. Hata yok; en büyük yükseklik farkı yaklaşık `6,96 × 10⁻¹¹ m`.
- Sekiz bölgede 2.439 ağaç: tür, yön, kök yüksekliği, boy ve dönüş eşleşti; en büyük kök farkı `3,26 × 10⁻¹⁰ m`.
- 1.531 uçuş/kayıt kontrolü: iniş, kalkış, duraklatma, 700/1.800 km/sa geçişi, beş warp mesafesi, üç saniye dolum, 12.000 km uzaya hareket, gezegen içinden geçen warp çarpışması, onarım, geçit, kaydet/aç ve bozuk SHA reddi. Bu sayı bağımsız oyun senaryosu sayısı değildir; zaman serisi örnekleri içerir.
- Unity kaydı özgün web okuyucusunda aynı coğrafya imzasıyla açıldı. Aynı yeni seed iki üretimde aynı sonucu verdi.
- Eski bölgesel paket ve model için mevcut üç Node testi geçti; atlanan test yok.
- C# 9 kaynakları kurulu Unity 6 ve URP API DLL'lerine karşı .NET derleyicisiyle kontrol edildi: sıfır hata/uyarı. Bu test shader veya Unity Player derlemesinin yerine geçmez.
- Dünya değişiminde arka plan işlerinin referansları sayılır. Eski çekirdeğin sahibi hemen bırakılır; son iş bittiğinde iş parçacığı stamp önbelleği serbest kalır. Bekleyen iş sırasında kapatma, yeni iş reddi ve tekrarlı kapatma ayrıca test edildi.

## Görsel kabulü engelleyen durum

Yeni sahnenin batch Unity denemesi ShaderGraph içe aktarımı sırasında bellek yetersizliğiyle kapandı. C: sürücüsünde son kontrolde yaklaşık 100 MiB boş yer vardı. Kullanıcının açık editörü ve kaydedilmemiş sahneleri kapatılmadı. Açık editör kaynakları henüz yenilemedi; otomatik editör bağlantısı yanıt vermedi ve Windows görsel kontrol aracı başlatılamadı. Yeni kaynaklar açık projeye kopyalandı. Editör kaynakları yenilediğinde, yalnız temiz bir sahne varsa yerel test isteği yeni sahneyi hazırlayıp görsel smoke testini çalıştırır.

Bu nedenle yeni sahnenin ekran görüntüsü, GPU zamanı, FPS, p95/p99 veya tam Windows build başarısı raporlanmadı. Eski sınırlı referans sahnenin ölçümleri yeni küresel sahneye mal edilmedi.

## Tam eşdeğerlik için kalan işler

1. Disk/bellek engeli giderildikten sonra gerçek Unity import, shader derlemesi, Play ve Windows Player doğrulaması; aynı kameralardan web/Unity karşılaştırması. Renk, bulut ışığı, gölge ve UI ölçeklerinin bu görüntüler üzerinde ayarlanması gerekiyor.
2. Yer şekilleri aynı olsa da yeni küresel çizici şu anda kalıcı mesh + CPU vertex upload kullanıyor. Webdeki ortak GPU grid/displacement yolu ve önceki bölgesel bake okuyucusu yeni küresel sahneye henüz bağlanmadı. Önceki referans sahnede bu paket/Jobs yolu korundu.
3. Parent LOD ve kenar etekleri mevcut; tam komşu kenar morph/stitch sözleşmesi ve sürekli geomorph henüz yok. Yakın uçuşta geçiş ve çarpışma/görüntü uyumu görsel olarak kontrol edilmeli.
4. Bulutlar özgün yoğunluk alanını kullanır; URP ışıklandırma ve gökyüzü bileşimi henüz webin tam atmosfer LUT/raymarch eşdeğeri değildir. Temporal reprojection henüz taşınmadı. Su shader'ında webin 64×64 FFT dalga çözümü yerine analitik yüzey dalgası var.
5. Geliştirici havza/akış görselleştirmesindeki tam analiz ızgarası, bütün deniz boğazı rota seçenekleri, ayar JSON içe/dışa aktarımı ve bazı ileri render seçenekleri kalan arayüz işleridir. Temel editör ve uçuş arayüzü kaynakta mevcuttur.
6. Küresel C# üretiminin Unity Mono/Burst performansı, resident mesh belleği, uzun uçuşta düğüm birikimi ve yükleme bütçesi ölçülmeli. Kalıcı kayıt önbelleği ve global mesh pooling için ölçüme dayalı çalışma gerekiyor. 100 FPS hedefi henüz doğrulanmadı.

Görsel kabulden önce bu çalışma deneysel aktarım olarak değerlendirilmelidir.
