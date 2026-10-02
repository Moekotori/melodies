#include "music_graph.hpp"

#include <algorithm>
#include <initializer_list>
#include <set>

namespace {
using Tags = std::map<std::string, std::vector<std::string>>;

std::optional<std::string> first(const Tags& tags, std::initializer_list<const char*> keys) {
  for (const char* key : keys) {
    const auto found = tags.find(key);
    if (found != tags.end() && !found->second.empty() && !found->second.front().empty()) {
      return found->second.front();
    }
  }
  return std::nullopt;
}

std::vector<std::string> values(const Tags& tags, std::initializer_list<const char*> keys) {
  std::vector<std::string> result;
  for (const char* key : keys) {
    const auto found = tags.find(key);
    if (found == tags.end()) continue;
    for (const auto& value : found->second) {
      if (!value.empty() && std::find(result.begin(), result.end(), value) == result.end()) {
        result.push_back(value);
      }
    }
  }
  return result;
}

bool hasUnknownGraphTag(const Tags& tags) {
  static const std::set<std::string> known = {
    // Match music-metadata's current VorbisTagMapper; leave unfamiliar graph tags to TS.
    "work", "composer", "conductor", "label", "discogs_label",
    "lyricist", "writer", "arranger", "producer", "engineer", "mixer", "remixer",
    "djmixer"
  };
  for (const auto& [key, unused] : tags) {
    (void)unused;
    if (known.count(key)) {
      if (unused.size() > 32) return true;
      continue;
    }
    if (key.find("work") != std::string::npos || key.find("movement") != std::string::npos
        || key.find("composer") != std::string::npos || key.find("conductor") != std::string::npos
        || key.find("performer") != std::string::npos || key.find("lyricist") != std::string::npos
        || key.find("arranger") != std::string::npos || key.find("producer") != std::string::npos
        || key.find("remixer") != std::string::npos || key.find("publisher") != std::string::npos
        || key.find("technician") != std::string::npos || key.find("label") != std::string::npos) return true;
  }
  return false;
}
} // namespace

std::optional<NativeMusicGraph> nativeMusicGraphFromVorbisTags(const Tags& tags) {
  if (hasUnknownGraphTag(tags)) return std::nullopt;
  const auto work = tags.find("work");
  if (work != tags.end() && work->second.size() > 1) return std::nullopt;
  if (tags.count("label") && tags.count("discogs_label")) return std::nullopt;
  NativeMusicGraph graph;
  graph.work = first(tags, { "work" });
  graph.composers = values(tags, { "composer" });
  graph.conductors = values(tags, { "conductor" });
  graph.labels = values(tags, { "label", "discogs_label" });
  if (graph.composers.size() > 32 || graph.conductors.size() > 32 || graph.labels.size() > 32) return std::nullopt;
  for (const auto& [role, key] : {
    std::pair<const char*, const char*>{ "lyricist", "lyricist" }, { "writer", "writer" },
    { "arranger", "arranger" }, { "producer", "producer" }, { "engineer", "engineer" },
    { "mixer", "mixer" }, { "remixer", "remixer" }, { "dj-mixer", "djmixer" }
  }) {
    for (const auto& value : values(tags, { key })) {
      graph.credits.push_back({ value, role, std::nullopt });
    }
  }
  if (graph.credits.size() > 64) return std::nullopt;
  return graph;
}
