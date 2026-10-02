#pragma once

#include <map>
#include <optional>
#include <string>
#include <vector>

struct NativeMusicCredit {
  std::string name;
  std::string role;
  std::optional<std::string> instrument;
};

struct NativeMusicGraph {
  std::optional<std::string> work;
  std::optional<std::string> movement;
  std::optional<int> movementNo;
  std::optional<int> movementTotal;
  std::vector<std::string> composers;
  std::vector<std::string> conductors;
  std::vector<NativeMusicCredit> performers;
  std::vector<std::string> labels;
  std::vector<NativeMusicCredit> credits;
};

// Null means an unrecognized graph-related tag must be supplemented by the TS reader.
std::optional<NativeMusicGraph> nativeMusicGraphFromVorbisTags(
  const std::map<std::string, std::vector<std::string>>& tags
);
