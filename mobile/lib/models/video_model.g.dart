// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'video_model.dart';

// **************************************************************************
// TypeAdapterGenerator
// **************************************************************************

class VideoModelAdapter extends TypeAdapter<VideoModel> {
  @override
  final int typeId = 0;

  @override
  VideoModel read(BinaryReader reader) {
    final numOfFields = reader.readByte();
    final fields = <int, dynamic>{
      for (int i = 0; i < numOfFields; i++) reader.readByte(): reader.read(),
    };
    return VideoModel(
      bvid: fields[0] as String,
      aid: fields[1] as int,
      title: fields[2] as String,
      author: fields[3] as String,
      mid: fields[4] as int,
      cover: fields[5] as String,
      duration: fields[6] as String,
      playCount: fields[7] as int,
      danmakuCount: fields[8] as int,
      favoriteCount: fields[9] as int,
      pubdate: fields[10] as int,
      addedAt: fields[11] as DateTime?,
    );
  }

  @override
  void write(BinaryWriter writer, VideoModel obj) {
    writer
      ..writeByte(12)
      ..writeByte(0)
      ..write(obj.bvid)
      ..writeByte(1)
      ..write(obj.aid)
      ..writeByte(2)
      ..write(obj.title)
      ..writeByte(3)
      ..write(obj.author)
      ..writeByte(4)
      ..write(obj.mid)
      ..writeByte(5)
      ..write(obj.cover)
      ..writeByte(6)
      ..write(obj.duration)
      ..writeByte(7)
      ..write(obj.playCount)
      ..writeByte(8)
      ..write(obj.danmakuCount)
      ..writeByte(9)
      ..write(obj.favoriteCount)
      ..writeByte(10)
      ..write(obj.pubdate)
      ..writeByte(11)
      ..write(obj.addedAt);
  }

  @override
  int get hashCode => typeId.hashCode;

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is VideoModelAdapter &&
          runtimeType == other.runtimeType &&
          typeId == other.typeId;
}
