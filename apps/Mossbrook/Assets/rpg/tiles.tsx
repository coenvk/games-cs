<?xml version="1.0" encoding="UTF-8"?>
<tileset version="1.10" tiledversion="1.10.2" name="tiles" tilewidth="16" tileheight="16" tilecount="80" columns="8">
 <image source="tiles.png" width="128" height="160"/>
 <tile id="0">
  <properties>
   <property name="name" value="grass"/>
  </properties>
 </tile>
 <tile id="1">
  <properties>
   <property name="name" value="grass_tall"/>
  </properties>
 </tile>
 <tile id="2">
  <properties>
   <property name="name" value="flowers"/>
  </properties>
 </tile>
 <tile id="3">
  <properties>
   <property name="name" value="path"/>
  </properties>
 </tile>
 <tile id="4">
  <properties>
   <property name="name" value="bridge"/>
  </properties>
 </tile>
 <tile id="5">
  <properties>
   <property name="name" value="water"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
  <animation>
   <frame tileid="5" duration="600"/>
   <frame tileid="64" duration="600"/>
  </animation>
 </tile>
 <tile id="6">
  <properties>
   <property name="name" value="tree"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="7">
  <properties>
   <property name="name" value="bush"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="8">
  <properties>
   <property name="name" value="fence"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="9">
  <properties>
   <property name="name" value="house_wall"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="10">
  <properties>
   <property name="name" value="roof"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="11">
  <properties>
   <property name="name" value="door"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="12">
  <properties>
   <property name="name" value="window"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="13">
  <properties>
   <property name="name" value="well"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="14">
  <properties>
   <property name="name" value="sign"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="15">
  <properties>
   <property name="name" value="hatch"/>
  </properties>
 </tile>
 <tile id="16">
  <properties>
   <property name="name" value="stone_floor"/>
  </properties>
 </tile>
 <tile id="17">
  <properties>
   <property name="name" value="stone_wall"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="18">
  <properties>
   <property name="name" value="stairs_down"/>
  </properties>
 </tile>
 <tile id="19">
  <properties>
   <property name="name" value="stairs_up"/>
  </properties>
 </tile>
 <tile id="20">
  <properties>
   <property name="name" value="rubble"/>
  </properties>
 </tile>
 <tile id="21">
  <properties>
   <property name="name" value="torch_wall"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
   <property name="light" type="float" value="4.5"/>
   <property name="light_color" value="#ffb060"/>
  </properties>
  <animation>
   <frame tileid="21" duration="180"/>
   <frame tileid="65" duration="140"/>
  </animation>
 </tile>
 <tile id="22">
  <properties>
   <property name="name" value="crate"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="23">
  <properties>
   <property name="name" value="barrel"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="24">
  <properties>
   <property name="name" value="player"/>
   <property name="walk" value="player_walk"/>
  </properties>
 </tile>
 <tile id="25">
  <properties>
   <property name="name" value="elder"/>
   <property name="walk" value="elder_walk"/>
  </properties>
 </tile>
 <tile id="26">
  <properties>
   <property name="name" value="herbalist"/>
   <property name="walk" value="herbalist_walk"/>
  </properties>
 </tile>
 <tile id="27">
  <properties>
   <property name="name" value="smith"/>
   <property name="walk" value="smith_walk"/>
  </properties>
 </tile>
 <tile id="28">
  <properties>
   <property name="name" value="villager"/>
   <property name="walk" value="villager_walk"/>
  </properties>
 </tile>
 <tile id="29">
  <properties>
   <property name="name" value="child"/>
   <property name="walk" value="child_walk"/>
  </properties>
 </tile>
 <tile id="30">
  <properties>
   <property name="name" value="rat"/>
   <property name="walk" value="rat_walk"/>
  </properties>
 </tile>
 <tile id="31">
  <properties>
   <property name="name" value="slime"/>
   <property name="walk" value="slime_walk"/>
  </properties>
 </tile>
 <tile id="32">
  <properties>
   <property name="name" value="skeleton"/>
   <property name="walk" value="skeleton_walk"/>
  </properties>
 </tile>
 <tile id="33">
  <properties>
   <property name="name" value="spider"/>
   <property name="walk" value="spider_walk"/>
  </properties>
 </tile>
 <tile id="34">
  <properties>
   <property name="name" value="potion"/>
  </properties>
 </tile>
 <tile id="35">
  <properties>
   <property name="name" value="glowcap"/>
   <property name="light" type="float" value="2.2"/>
   <property name="light_color" value="#60e8d0"/>
  </properties>
  <animation>
   <frame tileid="35" duration="500"/>
   <frame tileid="66" duration="500"/>
  </animation>
 </tile>
 <tile id="36">
  <properties>
   <property name="name" value="amulet"/>
  </properties>
 </tile>
 <tile id="37">
  <properties>
   <property name="name" value="sword"/>
  </properties>
 </tile>
 <tile id="38">
  <properties>
   <property name="name" value="shield"/>
  </properties>
 </tile>
 <tile id="39">
  <properties>
   <property name="name" value="coins"/>
  </properties>
 </tile>
 <tile id="40">
  <properties>
   <property name="name" value="bread"/>
  </properties>
 </tile>
 <tile id="41">
  <properties>
   <property name="name" value="armor"/>
  </properties>
 </tile>
 <tile id="42">
  <properties>
   <property name="name" value="wood_floor"/>
  </properties>
 </tile>
 <tile id="43">
  <properties>
   <property name="name" value="inner_wall"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="44">
  <properties>
   <property name="name" value="inner_window"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="45">
  <properties>
   <property name="name" value="fireplace"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="46">
  <properties>
   <property name="name" value="bed"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="47">
  <properties>
   <property name="name" value="table"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="48">
  <properties>
   <property name="name" value="chair"/>
  </properties>
 </tile>
 <tile id="49">
  <properties>
   <property name="name" value="bookshelf"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="50">
  <properties>
   <property name="name" value="rug"/>
  </properties>
 </tile>
 <tile id="51">
  <properties>
   <property name="name" value="anvil"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="52">
  <properties>
   <property name="name" value="forge"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="53">
  <properties>
   <property name="name" value="cauldron"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="54">
  <properties>
   <property name="name" value="plant_pot"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="55">
  <properties>
   <property name="name" value="cupboard"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="56">
  <properties>
   <property name="name" value="hay"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="57">
  <properties>
   <property name="name" value="sack"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="58">
  <properties>
   <property name="name" value="doormat"/>
  </properties>
 </tile>
 <tile id="59">
  <properties>
   <property name="name" value="old_woman"/>
   <property name="walk" value="old_woman_walk"/>
  </properties>
 </tile>
 <tile id="60">
  <properties>
   <property name="name" value="cat"/>
   <property name="walk" value="cat_walk"/>
  </properties>
 </tile>
 <tile id="64">
  <properties>
   <property name="name" value="water_2"/>
   <property name="solid" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="65">
  <properties>
   <property name="name" value="torch_wall_2"/>
   <property name="solid" type="bool" value="true"/>
   <property name="opaque" type="bool" value="true"/>
  </properties>
 </tile>
 <tile id="66">
  <properties>
   <property name="name" value="glowcap_2"/>
  </properties>
 </tile>
 <tile id="67">
  <properties>
   <property name="name" value="player_walk"/>
  </properties>
 </tile>
 <tile id="68">
  <properties>
   <property name="name" value="elder_walk"/>
  </properties>
 </tile>
 <tile id="69">
  <properties>
   <property name="name" value="herbalist_walk"/>
  </properties>
 </tile>
 <tile id="70">
  <properties>
   <property name="name" value="smith_walk"/>
  </properties>
 </tile>
 <tile id="71">
  <properties>
   <property name="name" value="villager_walk"/>
  </properties>
 </tile>
 <tile id="72">
  <properties>
   <property name="name" value="child_walk"/>
  </properties>
 </tile>
 <tile id="73">
  <properties>
   <property name="name" value="rat_walk"/>
  </properties>
 </tile>
 <tile id="74">
  <properties>
   <property name="name" value="slime_walk"/>
  </properties>
 </tile>
 <tile id="75">
  <properties>
   <property name="name" value="skeleton_walk"/>
  </properties>
 </tile>
 <tile id="76">
  <properties>
   <property name="name" value="spider_walk"/>
  </properties>
 </tile>
 <tile id="77">
  <properties>
   <property name="name" value="old_woman_walk"/>
  </properties>
 </tile>
 <tile id="78">
  <properties>
   <property name="name" value="cat_walk"/>
  </properties>
 </tile>
</tileset>
