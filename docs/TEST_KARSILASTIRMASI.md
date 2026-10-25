# LADİR / Unity — test sonuçları karşılaştırması

25 Ekim 2026. İncelenen kaynaklar: Unity `6a84c9e`, LADİR web `34841d6`.

**Sonuç:** İlk Unity prototipinin son kaydında başlangıç ayrıntısı daha erken hazır, uçuş kare süreleri daha düşük. Ancak kesintisiz 100 FPS doğrulanmadı. Tam oyunun Unity'ye geçiş kazancı da henüz ölçülmüş değil; içerik, ayrıntı düzeyi, rota ve ölçüm araçları eşit değil.

Bu rapor daha önce kaydedilen benchmark verilerinin yeniden hesaplanmasıdır; yeni bir GPU performans koşusu değildir. İşlevsel paket testleri bu incelemede ayrıca yeniden çalıştırıldı. Yüzdeler bu iki kayıt arasındaki farkı gösterir; tek tek değişikliklere veya yalnızca motor değişimine atfedilen kazanç değildir.

## 1. En güçlü karşılaştırma: Unity ilk görünür koşu / son koşu

İki kayıt da i7-9750H, GTX 1660 Ti, Direct3D 11 ve 1440 × 1000 kullanıyor. Aynı dünya kimliği, 32 × 32 km bölge, paketler ve programlı rota esas alınıyor. Son kurulum URP, 4× MSAA ve kapalı VSync kullanıyor. Yaklaşık 48 saniyelik beş uçuş aşaması, başlangıç aşamasından ayrılıyor.

Her sürümden yalnız bir kayıt var. Materyal düzenlemeleri, açılış logosunun kaldırılması, işletim sistemi önbelleği ve makinenin o andaki yükü etkileri ayrıştırılmadı. Gizli pencerenin görüntü üretmediği eski deneme karşılaştırmaya alınmadı.

### Açılış ve yükleme

| Ölçüt | İlk kayıt | Son kayıt | Kayıtlardaki azalma |
| --- | ---: | ---: | ---: |
| Sahne başlangıcı → ilk yüzey | 3,58 sn | 1,22 sn | %65,8 |
| Sahne başlangıcı → başlangıç ayrıntı planı hazır | 4,89 sn | 2,32 sn | %52,6 |
| Başlangıcın en uzun karesi | 3.056,37 ms | 786,69 ms | %74,3 |
| Tek mesh gönderiminin en yüksek CPU maliyeti | 20,55 ms | 10,09 ms | %50,9 |
| Koşu boyunca toplam mesh gönderimi CPU süresi | 478,04 ms | 452,53 ms | %5,3 |

Son koşuda uygulama işleminin açılışından itibaren ilk yüzey **2,73 sn**, başlangıç ayrıntısı **3,82 sn**. İlk koşuda bu işlem başlangıcı sayaçları yok; onlarla önce/sonra oranı hesaplanamaz. Başlangıç ayrıntısının hazır olması, bütün 32 km bölgenin en ince LOD'de bulunması anlamına gelmez.

**Başlangıç hâlâ takılıyor:** 50 ms üzerindeki kare sayısı 3'ten 4'e çıktı. En uzun takılma azaldı, fakat 786,69 ms kabul edilebilir bir uçuş karesi değil. Soğuk diskten ilk kurulum/açılış için ayrı ölçüm yapılmadı.

### Uçuş

FPS sütunu `1000 / medyan kare süresi` ile hesaplanır; ortalama veya garantili en düşük FPS değildir. p95/p99, karelerin sırasıyla %95/%99'unun altında kaldığı süreyi gösterir; düşük değer daha iyidir.

| Senaryo | Medyan FPS: ilk → son | Medyan ms: ilk → son | p95 ms: ilk → son | p99 ms: ilk → son |
| --- | ---: | ---: | ---: | ---: |
| Göl | 67 → 93 | 14,87 → 10,76 | 19,66 → 16,31 | 26,07 → 17,56 |
| Nehir uçuşu | 75 → 105 | 13,30 → 9,53 | 18,14 → 14,98 | 23,02 → 16,91 |
| Kanyon dönüşü | 84 → 108 | 11,96 → 9,27 | 17,51 → 14,69 | 19,54 → 16,32 |
| Dağ uçuşu | 92 → 123 | 10,83 → 8,16 | 17,04 → 13,99 | 19,28 → 15,56 |
| Programlı boost bırakma rotası | 72 → 94 | 13,95 → 10,63 | 18,70 → 15,59 | 23,14 → 17,53 |

Medyan kare süresi senaryoya göre **%22,5–28,4 azaldı**. Beş uçuş aşamasının toplamında:

| Ölçüt; başlangıç hariç | İlk kayıt | Son kayıt |
| --- | ---: | ---: |
| Örneklenen kare | 3.858 | 4.851 |
| 16,667 ms üzeri kare | 456 / %11,82 | 65 / %1,34 |
| 33,333 ms üzeri kare | 3 | 1 |
| 50 ms üzeri kare | 0 | 0 |
| En uzun uçuş karesi | 46,96 ms | 35,75 ms |

50 ms üzeri uçuş karesi iki kayıtta da yoktu; bu eşikte yeni bir kazanım iddia etmiyoruz. Boost bırakma aşamasının tek en uzun karesi ise **30,08 → 31,44 ms** yükseldi. İyileşme her ölçütte aynı yönde değil. Aşama yüzdeliklerinin ortalaması alınarak toplam uçuş p95/p99'u üretilmedi.

Kaynaklar: [ilk Unity kaydı](../evidence/unity-v1/visible/benchmark.json), [son Unity kaydı](../evidence/unity-v1/final/benchmark.json).

## 2. Tarayıcıdaki son iyileştirmeler ne başardı?

### V41 açılış: v1.25.0 → v1.26.0

Aynı kayıt ve aynı coğrafi kamera ile, uçuş düğmesine basıldıktan sonraki süreler:

| Ölçüt | Önce | Sonra |
| --- | ---: | ---: |
| Tam yakın ayrıntı, soğuk ziyaret | 16,18 sn | 12,30 sn |
| Tam yakın ayrıntı, sıcak ziyaret | 14,50 sn | 10,81 sn |
| Kokpit hazır, sıcak ziyaret | 4,59 sn | 2,21 sn |

Yakın ayrıntı beklemesi yaklaşık %24–25 azalmış; yine de 10–12 saniye kalmış. Kokpitin hazır olması arazinin bitmesiyle aynı olay değil. Editörün ilk açılması bu tablodan ayrıdır. [Önce](https://github.com/X111Metafizik/ladir/blob/34841d6abc1c1c12251c2ace18ffb853895db16f/evidence/v41/user-before/loading.json), [sonra](https://github.com/X111Metafizik/ladir/blob/34841d6abc1c1c12251c2ace18ffb853895db16f/evidence/v41/user-final/loading.json).

### V42 alçalma: v1.26.0 → v1.26.1

8.500 m'den 450 m'ye programlı yaklaşma yaklaşık 12,38 sn sürüyor. Her hücredeki sıra **soğuk / sıcak**:

| Ölçüt | Önce | Sonra |
| --- | ---: | ---: |
| Referans noktasında 3,81 m/hücre veya daha ince ayrıntı | 16,51 / 16,24 sn | 11,30 / 8,60 sn |
| Yaklaşma sonunda aynı ayrıntıyı ek bekleme | 4,13 / 3,85 sn | 0 / 0 sn |
| Bütün seçili kuyruğun bitişi | 16,59 / 16,24 sn | 21,53 / 17,08 sn |
| CPU çizim gönderimi p95 | 8,47 / 7,40 ms | 8,43 / 9,06 ms |
| Kare aralığı p99 | 49,92 / 33,25 ms | 49,99 / 50,05 ms |
| En uzun kare | 299,84 / 183,51 ms | 149,97 / 99,81 ms |
| 100 ms üzeri kare | 3 / 1 | 1 / 0 |
| İzlenen arazi CPU tamponları, tepe | 163,0 / 185,4 MiB | 204,0 / 201,7 MiB |

Ön yükleme gerekli noktayı daha erken tamamlamış ve en büyük takılmaları azaltmış. **Toplam kuyruk süresi, p99 ve bellek aynı ölçüde iyileşmemiş.** Sıcak koşunun p99'u kötüleşmiş. Soğuk yeni koşu son noktada 0,95 m/hücreye kadar ek ayrıntı yüklediği için biten iş miktarı da aynı değil.

Kayıt penceresi yaklaşma sonrasındaki kuyruk bitişini içerir; önce/sonra kayıt süreleri farklıdır. Bu testte GPU timestamp ölçümü yok. Sonuçlar bütün gezegenin veya ekranın tamamının hazır olduğunu göstermiyor. [Önce](https://github.com/X111Metafizik/ladir/blob/34841d6abc1c1c12251c2ace18ffb853895db16f/evidence/v42/baseline/descent.json), [sonra](https://github.com/X111Metafizik/ladir/blob/34841d6abc1c1c12251c2ace18ffb853895db16f/evidence/v42/release/descent.json).

### Tarayıcının ayrı 60 saniyelik uçuş rotası

V41 kaydı; V42 alçalma sonucuyla karıştırılmamalı:

| Koşu | CPU p95 | GPU render p95 | Kare p99 | En uzun kare | >100 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| Önce, soğuk | 12,24 ms | 7,14 ms | 33,35 ms | 233,25 ms | 1 |
| Sonra, soğuk | 9,33 ms | 5,93 ms | 17,13 ms | 266,51 ms | 3 |
| Önce, sıcak | 7,96 ms | 6,80 ms | 17,09 ms | 116,67 ms | 1 |
| Sonra, sıcak | 8,28 ms | 7,93 ms | 17,08 ms | 166,49 ms | 3 |

Soğuk p99 iyileşirken en kötü duraklamalar kötüleşmiş. Tarayıcı kaydı yaklaşık 58–59 FPS / 60 Hz sınırında; bu sayı donanımın gerçek maksimum render hızı değildir. GPU render süresi ile ekran sunumu ayrı ölçümlerdir. [İlk uçuş kaydı](https://github.com/X111Metafizik/ladir/blob/34841d6abc1c1c12251c2ace18ffb853895db16f/evidence/v41/flight-before/benchmark.json), [son uçuş kaydı](https://github.com/X111Metafizik/ladir/blob/34841d6abc1c1c12251c2ace18ffb853895db16f/evidence/v41/flight-final/benchmark.json).

## 3. Unity ile tarayıcıyı neden doğrudan oranlamıyoruz?

| Fark | Sonuca etkisi |
| --- | --- |
| Unity yalnız 32 × 32 km; tarayıcı küresel sistem | Yüklenen bölge, kuyruk ve sınır koşulları aynı değil. |
| Unity'de ağaç, düşman, hacimsel bulut ve küresel warp yok | Tam oyunun yükü henüz taşınmadı. |
| Unity paket örnekleri 7,8125 m; V42 referans mesh'i 0,95–3,81 m | Aynı dünya kimliği aynı siluet/mikro ayrıntı kalitesi demek değil. Mesh LOD ayrıca değişiyor. |
| Materyaller ve atmosfer farklı | GPU maliyeti ve görünüm eşit değil. |
| Unity rota VSync kapalı; tarayıcı yaklaşık 60 Hz | 59 FPS → 123 FPS doğrudan motor hızlanması olarak okunamaz. |
| Açılış saatlerinin başlangıcı ve tamamlanma ölçütleri farklı | 12,30 sn / 2,32 sn oranı geçerli bir motor kazancı değildir. |
| V41 uçuş fixture'ı farklı; V42 aynı dünya ama farklı konum/rota | Ortak dünya kimliği bile eşit kamera ve eşit iş yükü sağlamaz. |
| Unity CPU frame time bekleme içerebilir; web CPU sayacı gönderimi ölçer | CPU sayaçlarını doğrudan kıyaslamak veya CPU+GPU p95'i toplamak yanlış olur. |

Bu sonuçlar, bölgesel hazır veriyle Unity prototipinin hızlı açılabildiğini gösteriyor. Aynı görünümde tam oyunun kaç kat hızlanacağını göstermiyor.

## 4. 100 FPS hedefine kalan mesafe

100 FPS için kare bütçesi **10 ms**. Son Unity koşusunda beş senaryodan üçünün medyanı bu bütçeye uyuyor; **hiçbirinin p95'i uymuyor**. p95'i 10 ms'ye indirmek için gözlenen p95 sürelerinin yaklaşık **%28,5–38,7** daha düşmesi gerekiyor. Bu, gereken süre azalmasının hesabıdır; uygulanacak bir tekniğin garantili kazancı değildir. p95 hedefini karşılamak da her karenin 100 FPS üzerinde olmasını garanti etmez.

| Öncelik | Kanıt ve yapılacak ölçüm |
| --- | --- |
| 1. Gerçek uçuşla karşılaştırılabilir test | Unity benchmark'ında boost bırakma konumu sabit 300 m/s ilerliyor; hız/ön yükleme parametresi 500 → 194,444 m/s değişiyor. Gerçek ivmelenme ve çarpışma yolu devre dışı. Aynı konumdan gerçek kontrol girdileriyle boost/iniş/alçalma kaydı alınmalı. |
| 2. Başlangıçtaki uzun kare | Son kayıtta 786,69 ms. Şu sayaçlar bunun shader, asset veya geometriye dağılımını kanıtlamıyor. Unity Profiler ile ilk kullanımlar ayrı işaretlenmeli. |
| 3. Büyük mesh gönderimi | Tek gönderim CPU'da 10,09 ms; 2 ms hedefini ve tek başına 10 ms kare bütçesini zorluyor. Kalıcı tampon ve küçük gönderim dilimleri deneyi ayrı ölçülmeli. |
| 4. GPU yükü | Son senaryoların GPU medyanı 7,43–9,25 ms. Gölge, su ve arazi materyali maliyeti ayrı geçiş ölçümleriyle ayrıştırılmalı; mevcut rapor hangisinin baskın olduğunu kanıtlamıyor. |
| 5. Görsel ayrıntı eşitliği | Gemi ve coğrafya aktarılmış olsa da kıyı basamakları, sert kar maskesi ve düzgün görünen yüzeyler var. Yakın örnekleme ve materyal kalitesi eşitlenmeden hız karşılaştırması tamamlanmış sayılmamalı. |
| 6. LOD ve bellek | Kenar sözleşmeleri test edildi; geomorph yok. GPU mesh baytları izleniyor ancak ayrı sert GPU bütçesi yok. Gerçek alçalma ve uzun uçuşla geçişler/RAM eğrisi ölçülmeli. |

Önerilen kabul deneyi: aynı kamera/FOV, aynı metre/hücre hedefi ve aynı efektler; en az beş tekrarlı A/B koşu; ayrı soğuk/sıcak önbellek; zamanlanan bölümde ekran görüntüsü almama; kare bazında iz ve 10 ms üzeri kare oranı; ayrıca 15–30 dakika gerçek uçuş. Bunlar henüz tamamlanmış testler değildir.

## 5. Bellek, paket ve işlevsel doğrulama

Unity ilk → son:

- Örnek önbelleği tepe: **95,98 → 95,98 MiB**.
- Hesaplanan mesh GPU tamponları tepe: **117,21 → 117,21 MiB**.
- Unity tahsis sayacı: yaklaşık **206,41 → 206,40 MiB**.
- Koşu sonunda yönetilen heap: **10,52 → 9,20 MiB**; tek anlık ölçüm, sızıntı testi değil.
- İşlem working-set sorgusu 0 dönmüş: **ölçüm kullanılamıyor**. Yukarıdaki sayaçlar birbiriyle örtüşebilir; toplanıp toplam RAM/VRAM diye sunulamaz.
- Bölge paketi: **53,68 MiB sıkıştırılmış**, **196,52 MiB ham**, 256 tile. Yeniden üretim **82,78 sn**; önceki üretim 104,7 sn. Bu çevrimdışı üretim süresidir, oyun açılışı değildir.

| Kontrol | Sonuç | Kapsam sınırı |
| --- | --- | --- |
| Unity Windows derlemesi | 0 hata, 0 uyarı | Önceki doğrulanmış derleme; bu raporda yeniden build yapılmadı. |
| 256 paket / SHA-256 / başlıklar | Geçti | Dosyaların ve kimliğin doğruluğu. |
| 123.360 ortak yükseklik/su kenarı, 484 grid sözleşmesi | Geçti | Matematiksel kenar tutarlılığı; her kamerada görsel test anlamına gelmez. |
| Gerçek Update yolunda sekiz giriş senaryosu | Geçti | Boost, seyir, iniş, kalkış, hasar, onarım, orbital kamera, duraklatma. Uzun süreli performans testi değil. |
| Unity Node kontrolleri | Yerelde 3/3 geçti | Web kaynağı verilince üretici eşleşme testi de çalışır. |
| GitHub paket kontrolü | 2 geçti, 1 koşullu kontrol atlanır | CI'da ayrı web checkout'u yok; başarı bütün Unity oyununu derlediği anlamına gelmez. |
| Web V42 regresyonu | Kayıtlı 228/228 geçti | Unity test sayısıyla kıyaslanarak motor kalitesi ölçülemez. |
| WebGPU smoke | Kayıtlı başarı, 154 shader modülü | Editör/uçuş/warp/onarım; 100 FPS garantisi değil. |

Kaynaklar: [Unity veri kontrolü](../evidence/unity-v1/validation.json), [kontrol senaryoları](../evidence/unity-v1/controls/controls.json), [derleme](../evidence/unity-v1/build.json), [paket üretimi](../evidence/unity-v1/export.json), [web V42 doğrulaması](https://github.com/X111Metafizik/ladir/blob/34841d6abc1c1c12251c2ace18ffb853895db16f/evidence/v42/validation.json), [ilk GitHub koşusu](https://github.com/X111Metafizik/Unity/actions/runs/40150356889).

## 6. Yeniden hesaplama ve veri sınırları

```sh
node tools/compare-benchmarks.mjs --web-source "../ladir"
```

[Hesaplanan karşılaştırma verisi](../evidence/comparison-v1/summary.json), kullanılan her JSON'un SHA-256 değerini ve bütün oranların dayandığı sayıları içerir. [Hesaplama kodu](../tools/compare-benchmarks.mjs) performans testi çalıştırmaz.

Unity kayıtlarında kare bazındaki iz saklanmamış; yalnız aşama özetleri var. Bu nedenle tüm koşunun kesin p95/p99'u, 10 ms üzerindeki kare oranı ve uzun karenin çağrı yığını bu veriden çıkarılamaz. Ekran görüntüsü alınan Unity kareleri ölçüme dahildir. Okuma ve job gecikmeleri eşzamanlı işlere aittir; toplamları açılış veya kare süresi değildir.

Mevcut kanıtlarla en doğru karar, Unity prototipini geliştirmeye devam ederken önce eşit ayrıntılı gerçek uçuş ölçümünü kurmaktır. Kesintisiz 100 FPS, tam oyun eşdeğerliği ve uzun süreli bellek kararlılığı henüz açık hedeflerdir.
