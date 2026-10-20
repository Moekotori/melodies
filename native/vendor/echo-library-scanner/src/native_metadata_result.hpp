#pragma once
#include "music_graph.hpp"
#include <map>
#include <optional>
#include <string>
#include <vector>

struct NativeMetadataResult {
  std::string title, artist, album, albumArtist;
  std::optional<int> trackNo, discNo, year;
  std::optional<std::string> genre;
  double duration = 0;
  std::optional<int> sampleRate, bitDepth, bitrate;
  bool mqa = false;
  std::optional<double> bpm, replayGainTrackGainDb, replayGainAlbumGainDb;
  std::optional<double> replayGainTrackPeak, replayGainAlbumPeak, replayGainIntegratedLufs;
  std::vector<unsigned char> embeddedCover;
  std::string embeddedCoverMimeType;
  std::string codec = "FLAC";
  bool hasVorbisComments = false, hasEmbeddedMetadata = false;
  std::map<std::string, std::string> sources;
  std::optional<NativeMusicGraph> musicGraph;
  std::map<std::string, std::vector<std::string>> creditTags;
  bool creditTagsExceeded = false;
};
