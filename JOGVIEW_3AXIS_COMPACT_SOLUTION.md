# JogView 3-Axis Compact Layout Solution

## Problem
3 eksenli makinelerde (A ekseni olmayan) A ekseni panellerinin boş bıraktığı alan optimize edilmeli.

## Denenen Çözümler

### ❌ Çözüm 1: Dinamik RowSpan (Başarısız)
X-Y, Z, Speed panellerine `RowSpan=2` ekleyip A ekseni yokken genişletme.
- **Sorun:** Layout karmaşıklaştı ve görsel bozukluklar oluştu.

### ✅ Çözüm 2: XYZ Step Control Repositioning (Başarılı)
XYZ Step Control panelini A ekseni yokken Row 3'e kaydırıp 3 sütun kaplar.
- **Durum:** Zaten uygulanmış ve çalışıyor.

### 🆕 Çözüm 3: Row Yükseliklerini Optimize Etme (Önerilen)

#### Mevcut Row Yükseklikleri:
```xml
<Grid.RowDefinitions>
    <RowDefinition Height="80*"/>   <!-- Row 0: Control Buttons -->
    <RowDefinition Height="95*"/>   <!-- Row 1: Spindle Panel -->
    <RowDefinition Height="492*"/>  <!-- Row 2: Main Jog Panels -->
  <RowDefinition Height="273*"/>  <!-- Row 3: A-Axis Panels -->
</Grid.RowDefinitions>
```

**Toplam:** 80 + 95 + 492 + 273 = 940 birim

#### 3 Eksenli Makine için Önerilen Değerler:
A ekseni yokken Row 3 boş kalıyor (273 birim). Bu alanı Row 0 ve Row 1'i küçülterek telafi edebiliriz:

```xml
<!-- 4 eksenli makine için: -->
Row 0: 80*  (8.5%)
Row 1: 95*  (10.1%)
Row 2: 492* (52.3%)
Row 3: 273* (29%)

<!-- 3 eksenli makine için önerilen: -->
Row 0: 60*  (6.4%)  → 20 birim azaltıldı
Row 1: 75*  (8.0%)  → 20 birim azaltıldı
Row 2: 625* (66.5%) → 133 birim arttırıldı
Row 3: 180* (19.1%) → 93 birim azaltıldı (XYZ Step Control için yeterli)

Toplam: 60 + 75 + 625 + 180 = 940 birim (aynı)
```

## Uygulama Stratejisi

### Seçenek A: Conditional Style ile Dinamik Row Heights
```xml
<Grid x:Name="MainGrid" Margin="0,5,0,0">
    <Grid.Style>
    <Style TargetType="Grid">
       <Setter Property="Grid.RowDefinitions">
                <Setter.Value>
          <RowDefinitionCollection>
         <RowDefinition Height="80*"/>
<RowDefinition Height="95*"/>
      <RowDefinition Height="492*"/>
<RowDefinition Height="273*"/>
      </RowDefinitionCollection>
                </Setter.Value>
       </Setter>
 <Style.Triggers>
            <DataTrigger Binding="{Binding IsAAxisAvailable}" Value="False">
     <Setter Property="Grid.RowDefinitions">
         <Setter.Value>
     <RowDefinitionCollection>
 <RowDefinition Height="60*"/>
           <RowDefinition Height="75*"/>
        <RowDefinition Height="625*"/>
        <RowDefinition Height="180*"/>
</RowDefinitionCollection>
        </Setter.Value>
         </Setter>
 </DataTrigger>
   </Style.Triggers>
      </Style>
    </Grid.Style>
    <!-- ... -->
</Grid>
```

**NOT:** Bu yaklaşım WPF'de `Grid.RowDefinitions` için `DataTrigger` ile desteklenmez! ❌

### Seçenek B: Button Height Azaltma (Basit ve Etkili) ✅
Row yüksekliklerini değiştirmek yerine, butonların yüksekliklerini dinamik olarak azalt:

```xml
<!-- Control Buttons -->
<ToggleButton Height="55">
    <ToggleButton.Style>
     <Style TargetType="ToggleButton" BasedOn="{StaticResource ControlToggleButtonBorderOnlyStyle}">
        <Setter Property="Height" Value="55"/>
          <Style.Triggers>
  <DataTrigger Binding="{Binding IsAAxisAvailable}" Value="False">
  <Setter Property="Height" Value="45"/>
    </DataTrigger>
      </Style.Triggers>
        </Style>
    </ToggleButton.Style>
</ToggleButton>
```

### Seçenek C: Manuel Grid.RowDefinitions Değişikliği (Kabul Edilebilir)
3 eksenli makineler için Row yüksekliklerini manuel olarak ayarla:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="70*"/>   <!-- Row 0: 10 birim azaltıldı -->
    <RowDefinition Height="85*"/>   <!-- Row 1: 10 birim azaltıldı -->
    <RowDefinition Height="560*"/>  <!-- Row 2: 68 birim arttırıldı -->
    <RowDefinition Height="225*"/>  <!-- Row 3: 48 birim azaltıldı -->
</Grid.RowDefinitions>
```

## Önerilen Çözüm: Manuel Row Ayarlama

**Row yüksekliklerini biraz optimize edelim:**

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="70*"/>   <!-- Row 0: Control Buttons (10 birim azaltıldı) -->
    <RowDefinition Height="85*"/>   <!-- Row 1: Spindle Panel (10 birim azaltıldı) -->
    <RowDefinition Height="560*"/>  <!-- Row 2: Main Panels (68 birim arttırıldı) -->
    <RowDefinition Height="225*"/>  <!-- Row 3: A-Axis / XYZ Step (48 birim azaltıldı) -->
</Grid.RowDefinitions>
```

### Avantajlar:
✅ Tüm makinelerde çalışır (4 ve 3 eksenli)  
✅ Row 2 (X-Y, Z, Speed) panellerine daha fazla alan  
✅ Row 0 ve Row 1 biraz daha kompakt  
✅ A ekseni yokken XYZ Step Control için yeterli alan (Row 3)  
✅ Kod değişikliği minimal  

### Dezavantajlar:
⚠️ 4 eksenli makinelerde de uygulanır (farklı davranış yok)  
⚠️ Butonlar biraz daha küçük görünebilir  

## Sonuç

**Kabul edilebilir çözüm:** Manuel Row Ayarlama  
**Alternatif:** Mevcut layout'u değiştirmeden kullanmaya devam et (zaten XYZ Step Control dinamik çalışıyor)

