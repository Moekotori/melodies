#pragma once
#include "native_metadata_result.hpp"
#include <algorithm>
#include <cctype>
#include <set>
inline bool isCreditTag(std::string key) {
  key.erase(std::remove_if(key.begin(), key.end(), [](char c) { return c == '_' || c == ' '; }), key.end());
  std::transform(key.begin(), key.end(), key.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
  static const std::set<std::string> supported = { "artist", "albumartist", "albumperformer", "performer", "personnel", "featuring", "soloist", "soloists", "ensemble", "composer", "lyricist", "writer", "arranger", "conductor", "producer", "engineer", "mixer", "mixedby", "remixer", "remixedby", "djmixer" };
  return supported.count(key) != 0;
}
inline void collectCreditTags(const std::map<std::string,std::vector<std::string>>& tags, NativeMetadataResult& result) {
  std::size_t count = 0, bytes = 0;
  for (const auto& [key, values] : tags) if (isCreditTag(key)) for (const auto& value : values) {
    bytes += value.size();
    // Conservative UTF-8 envelope; the managed boundary applies the exact UTF-16 limits.
    if (++count > 64 || value.size() > 8192 || bytes > 65536) { result.creditTagsExceeded = true; return; }
    result.creditTags[key].push_back(value);
  }
}
