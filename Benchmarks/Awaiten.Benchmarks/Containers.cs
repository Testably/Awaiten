// Benchmark fixtures: 256 marker service types and three generated containers of increasing
// size, used by ResolveBenchmarks / BuildBenchmarks to measure dispatch latency as the
// registration count grows.
using Awaiten;

namespace Awaiten.Benchmarks;

public sealed class B0;
public sealed class B1;
public sealed class B2;
public sealed class B3;
public sealed class B4;
public sealed class B5;
public sealed class B6;
public sealed class B7;
public sealed class B8;
public sealed class B9;
public sealed class B10;
public sealed class B11;
public sealed class B12;
public sealed class B13;
public sealed class B14;
public sealed class B15;
public sealed class B16;
public sealed class B17;
public sealed class B18;
public sealed class B19;
public sealed class B20;
public sealed class B21;
public sealed class B22;
public sealed class B23;
public sealed class B24;
public sealed class B25;
public sealed class B26;
public sealed class B27;
public sealed class B28;
public sealed class B29;
public sealed class B30;
public sealed class B31;
public sealed class B32;
public sealed class B33;
public sealed class B34;
public sealed class B35;
public sealed class B36;
public sealed class B37;
public sealed class B38;
public sealed class B39;
public sealed class B40;
public sealed class B41;
public sealed class B42;
public sealed class B43;
public sealed class B44;
public sealed class B45;
public sealed class B46;
public sealed class B47;
public sealed class B48;
public sealed class B49;
public sealed class B50;
public sealed class B51;
public sealed class B52;
public sealed class B53;
public sealed class B54;
public sealed class B55;
public sealed class B56;
public sealed class B57;
public sealed class B58;
public sealed class B59;
public sealed class B60;
public sealed class B61;
public sealed class B62;
public sealed class B63;
public sealed class B64;
public sealed class B65;
public sealed class B66;
public sealed class B67;
public sealed class B68;
public sealed class B69;
public sealed class B70;
public sealed class B71;
public sealed class B72;
public sealed class B73;
public sealed class B74;
public sealed class B75;
public sealed class B76;
public sealed class B77;
public sealed class B78;
public sealed class B79;
public sealed class B80;
public sealed class B81;
public sealed class B82;
public sealed class B83;
public sealed class B84;
public sealed class B85;
public sealed class B86;
public sealed class B87;
public sealed class B88;
public sealed class B89;
public sealed class B90;
public sealed class B91;
public sealed class B92;
public sealed class B93;
public sealed class B94;
public sealed class B95;
public sealed class B96;
public sealed class B97;
public sealed class B98;
public sealed class B99;
public sealed class B100;
public sealed class B101;
public sealed class B102;
public sealed class B103;
public sealed class B104;
public sealed class B105;
public sealed class B106;
public sealed class B107;
public sealed class B108;
public sealed class B109;
public sealed class B110;
public sealed class B111;
public sealed class B112;
public sealed class B113;
public sealed class B114;
public sealed class B115;
public sealed class B116;
public sealed class B117;
public sealed class B118;
public sealed class B119;
public sealed class B120;
public sealed class B121;
public sealed class B122;
public sealed class B123;
public sealed class B124;
public sealed class B125;
public sealed class B126;
public sealed class B127;
public sealed class B128;
public sealed class B129;
public sealed class B130;
public sealed class B131;
public sealed class B132;
public sealed class B133;
public sealed class B134;
public sealed class B135;
public sealed class B136;
public sealed class B137;
public sealed class B138;
public sealed class B139;
public sealed class B140;
public sealed class B141;
public sealed class B142;
public sealed class B143;
public sealed class B144;
public sealed class B145;
public sealed class B146;
public sealed class B147;
public sealed class B148;
public sealed class B149;
public sealed class B150;
public sealed class B151;
public sealed class B152;
public sealed class B153;
public sealed class B154;
public sealed class B155;
public sealed class B156;
public sealed class B157;
public sealed class B158;
public sealed class B159;
public sealed class B160;
public sealed class B161;
public sealed class B162;
public sealed class B163;
public sealed class B164;
public sealed class B165;
public sealed class B166;
public sealed class B167;
public sealed class B168;
public sealed class B169;
public sealed class B170;
public sealed class B171;
public sealed class B172;
public sealed class B173;
public sealed class B174;
public sealed class B175;
public sealed class B176;
public sealed class B177;
public sealed class B178;
public sealed class B179;
public sealed class B180;
public sealed class B181;
public sealed class B182;
public sealed class B183;
public sealed class B184;
public sealed class B185;
public sealed class B186;
public sealed class B187;
public sealed class B188;
public sealed class B189;
public sealed class B190;
public sealed class B191;
public sealed class B192;
public sealed class B193;
public sealed class B194;
public sealed class B195;
public sealed class B196;
public sealed class B197;
public sealed class B198;
public sealed class B199;
public sealed class B200;
public sealed class B201;
public sealed class B202;
public sealed class B203;
public sealed class B204;
public sealed class B205;
public sealed class B206;
public sealed class B207;
public sealed class B208;
public sealed class B209;
public sealed class B210;
public sealed class B211;
public sealed class B212;
public sealed class B213;
public sealed class B214;
public sealed class B215;
public sealed class B216;
public sealed class B217;
public sealed class B218;
public sealed class B219;
public sealed class B220;
public sealed class B221;
public sealed class B222;
public sealed class B223;
public sealed class B224;
public sealed class B225;
public sealed class B226;
public sealed class B227;
public sealed class B228;
public sealed class B229;
public sealed class B230;
public sealed class B231;
public sealed class B232;
public sealed class B233;
public sealed class B234;
public sealed class B235;
public sealed class B236;
public sealed class B237;
public sealed class B238;
public sealed class B239;
public sealed class B240;
public sealed class B241;
public sealed class B242;
public sealed class B243;
public sealed class B244;
public sealed class B245;
public sealed class B246;
public sealed class B247;
public sealed class B248;
public sealed class B249;
public sealed class B250;
public sealed class B251;
public sealed class B252;
public sealed class B253;
public sealed class B254;
public sealed class B255;

[Container]
[Singleton<B0>]
[Singleton<B1>]
[Singleton<B2>]
[Singleton<B3>]
[Singleton<B4>]
[Singleton<B5>]
[Singleton<B6>]
[Singleton<B7>]
public sealed partial class GenContainer8;

[Container]
[Singleton<B0>]
[Singleton<B1>]
[Singleton<B2>]
[Singleton<B3>]
[Singleton<B4>]
[Singleton<B5>]
[Singleton<B6>]
[Singleton<B7>]
[Singleton<B8>]
[Singleton<B9>]
[Singleton<B10>]
[Singleton<B11>]
[Singleton<B12>]
[Singleton<B13>]
[Singleton<B14>]
[Singleton<B15>]
[Singleton<B16>]
[Singleton<B17>]
[Singleton<B18>]
[Singleton<B19>]
[Singleton<B20>]
[Singleton<B21>]
[Singleton<B22>]
[Singleton<B23>]
[Singleton<B24>]
[Singleton<B25>]
[Singleton<B26>]
[Singleton<B27>]
[Singleton<B28>]
[Singleton<B29>]
[Singleton<B30>]
[Singleton<B31>]
[Singleton<B32>]
[Singleton<B33>]
[Singleton<B34>]
[Singleton<B35>]
[Singleton<B36>]
[Singleton<B37>]
[Singleton<B38>]
[Singleton<B39>]
[Singleton<B40>]
[Singleton<B41>]
[Singleton<B42>]
[Singleton<B43>]
[Singleton<B44>]
[Singleton<B45>]
[Singleton<B46>]
[Singleton<B47>]
[Singleton<B48>]
[Singleton<B49>]
[Singleton<B50>]
[Singleton<B51>]
[Singleton<B52>]
[Singleton<B53>]
[Singleton<B54>]
[Singleton<B55>]
[Singleton<B56>]
[Singleton<B57>]
[Singleton<B58>]
[Singleton<B59>]
[Singleton<B60>]
[Singleton<B61>]
[Singleton<B62>]
[Singleton<B63>]
public sealed partial class GenContainer64;

[Container]
[Singleton<B0>]
[Singleton<B1>]
[Singleton<B2>]
[Singleton<B3>]
[Singleton<B4>]
[Singleton<B5>]
[Singleton<B6>]
[Singleton<B7>]
[Singleton<B8>]
[Singleton<B9>]
[Singleton<B10>]
[Singleton<B11>]
[Singleton<B12>]
[Singleton<B13>]
[Singleton<B14>]
[Singleton<B15>]
[Singleton<B16>]
[Singleton<B17>]
[Singleton<B18>]
[Singleton<B19>]
[Singleton<B20>]
[Singleton<B21>]
[Singleton<B22>]
[Singleton<B23>]
[Singleton<B24>]
[Singleton<B25>]
[Singleton<B26>]
[Singleton<B27>]
[Singleton<B28>]
[Singleton<B29>]
[Singleton<B30>]
[Singleton<B31>]
[Singleton<B32>]
[Singleton<B33>]
[Singleton<B34>]
[Singleton<B35>]
[Singleton<B36>]
[Singleton<B37>]
[Singleton<B38>]
[Singleton<B39>]
[Singleton<B40>]
[Singleton<B41>]
[Singleton<B42>]
[Singleton<B43>]
[Singleton<B44>]
[Singleton<B45>]
[Singleton<B46>]
[Singleton<B47>]
[Singleton<B48>]
[Singleton<B49>]
[Singleton<B50>]
[Singleton<B51>]
[Singleton<B52>]
[Singleton<B53>]
[Singleton<B54>]
[Singleton<B55>]
[Singleton<B56>]
[Singleton<B57>]
[Singleton<B58>]
[Singleton<B59>]
[Singleton<B60>]
[Singleton<B61>]
[Singleton<B62>]
[Singleton<B63>]
[Singleton<B64>]
[Singleton<B65>]
[Singleton<B66>]
[Singleton<B67>]
[Singleton<B68>]
[Singleton<B69>]
[Singleton<B70>]
[Singleton<B71>]
[Singleton<B72>]
[Singleton<B73>]
[Singleton<B74>]
[Singleton<B75>]
[Singleton<B76>]
[Singleton<B77>]
[Singleton<B78>]
[Singleton<B79>]
[Singleton<B80>]
[Singleton<B81>]
[Singleton<B82>]
[Singleton<B83>]
[Singleton<B84>]
[Singleton<B85>]
[Singleton<B86>]
[Singleton<B87>]
[Singleton<B88>]
[Singleton<B89>]
[Singleton<B90>]
[Singleton<B91>]
[Singleton<B92>]
[Singleton<B93>]
[Singleton<B94>]
[Singleton<B95>]
[Singleton<B96>]
[Singleton<B97>]
[Singleton<B98>]
[Singleton<B99>]
[Singleton<B100>]
[Singleton<B101>]
[Singleton<B102>]
[Singleton<B103>]
[Singleton<B104>]
[Singleton<B105>]
[Singleton<B106>]
[Singleton<B107>]
[Singleton<B108>]
[Singleton<B109>]
[Singleton<B110>]
[Singleton<B111>]
[Singleton<B112>]
[Singleton<B113>]
[Singleton<B114>]
[Singleton<B115>]
[Singleton<B116>]
[Singleton<B117>]
[Singleton<B118>]
[Singleton<B119>]
[Singleton<B120>]
[Singleton<B121>]
[Singleton<B122>]
[Singleton<B123>]
[Singleton<B124>]
[Singleton<B125>]
[Singleton<B126>]
[Singleton<B127>]
[Singleton<B128>]
[Singleton<B129>]
[Singleton<B130>]
[Singleton<B131>]
[Singleton<B132>]
[Singleton<B133>]
[Singleton<B134>]
[Singleton<B135>]
[Singleton<B136>]
[Singleton<B137>]
[Singleton<B138>]
[Singleton<B139>]
[Singleton<B140>]
[Singleton<B141>]
[Singleton<B142>]
[Singleton<B143>]
[Singleton<B144>]
[Singleton<B145>]
[Singleton<B146>]
[Singleton<B147>]
[Singleton<B148>]
[Singleton<B149>]
[Singleton<B150>]
[Singleton<B151>]
[Singleton<B152>]
[Singleton<B153>]
[Singleton<B154>]
[Singleton<B155>]
[Singleton<B156>]
[Singleton<B157>]
[Singleton<B158>]
[Singleton<B159>]
[Singleton<B160>]
[Singleton<B161>]
[Singleton<B162>]
[Singleton<B163>]
[Singleton<B164>]
[Singleton<B165>]
[Singleton<B166>]
[Singleton<B167>]
[Singleton<B168>]
[Singleton<B169>]
[Singleton<B170>]
[Singleton<B171>]
[Singleton<B172>]
[Singleton<B173>]
[Singleton<B174>]
[Singleton<B175>]
[Singleton<B176>]
[Singleton<B177>]
[Singleton<B178>]
[Singleton<B179>]
[Singleton<B180>]
[Singleton<B181>]
[Singleton<B182>]
[Singleton<B183>]
[Singleton<B184>]
[Singleton<B185>]
[Singleton<B186>]
[Singleton<B187>]
[Singleton<B188>]
[Singleton<B189>]
[Singleton<B190>]
[Singleton<B191>]
[Singleton<B192>]
[Singleton<B193>]
[Singleton<B194>]
[Singleton<B195>]
[Singleton<B196>]
[Singleton<B197>]
[Singleton<B198>]
[Singleton<B199>]
[Singleton<B200>]
[Singleton<B201>]
[Singleton<B202>]
[Singleton<B203>]
[Singleton<B204>]
[Singleton<B205>]
[Singleton<B206>]
[Singleton<B207>]
[Singleton<B208>]
[Singleton<B209>]
[Singleton<B210>]
[Singleton<B211>]
[Singleton<B212>]
[Singleton<B213>]
[Singleton<B214>]
[Singleton<B215>]
[Singleton<B216>]
[Singleton<B217>]
[Singleton<B218>]
[Singleton<B219>]
[Singleton<B220>]
[Singleton<B221>]
[Singleton<B222>]
[Singleton<B223>]
[Singleton<B224>]
[Singleton<B225>]
[Singleton<B226>]
[Singleton<B227>]
[Singleton<B228>]
[Singleton<B229>]
[Singleton<B230>]
[Singleton<B231>]
[Singleton<B232>]
[Singleton<B233>]
[Singleton<B234>]
[Singleton<B235>]
[Singleton<B236>]
[Singleton<B237>]
[Singleton<B238>]
[Singleton<B239>]
[Singleton<B240>]
[Singleton<B241>]
[Singleton<B242>]
[Singleton<B243>]
[Singleton<B244>]
[Singleton<B245>]
[Singleton<B246>]
[Singleton<B247>]
[Singleton<B248>]
[Singleton<B249>]
[Singleton<B250>]
[Singleton<B251>]
[Singleton<B252>]
[Singleton<B253>]
[Singleton<B254>]
[Singleton<B255>]
public sealed partial class GenContainer256;

