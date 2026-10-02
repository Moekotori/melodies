#pragma once

extern "C" {
#include <libavutil/dict.h>
}

#include <algorithm>
#include <cctype>
#include <string>

namespace echo {

inline std::string normalizeMqaMetadataKey(const char* key)
{
    std::string normalized;
    if (key == nullptr)
        return normalized;
    for (const unsigned char ch : std::string(key))
    {
        if (std::isalnum(ch) != 0)
            normalized.push_back(static_cast<char>(std::tolower(ch)));
    }
    return normalized;
}

inline bool isTruthyMqaMetadataValue(const char* value)
{
    if (value == nullptr)
        return false;
    std::string normalized(value);
    normalized.erase(normalized.begin(), std::find_if(normalized.begin(), normalized.end(), [](unsigned char ch) {
        return std::isspace(ch) == 0;
    }));
    normalized.erase(std::find_if(normalized.rbegin(), normalized.rend(), [](unsigned char ch) {
        return std::isspace(ch) == 0;
    }).base(), normalized.end());
    std::transform(normalized.begin(), normalized.end(), normalized.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });
    return ! normalized.empty()
        && normalized != "0"
        && normalized != "false"
        && normalized != "no"
        && normalized != "off"
        && normalized != "none";
}

inline bool hasMqaEncoderMetadata(const AVDictionary* metadata)
{
    const AVDictionaryEntry* entry = nullptr;
    while ((entry = av_dict_iterate(metadata, entry)) != nullptr)
    {
        const std::string key = normalizeMqaMetadataKey(entry->key);
        const bool encoderKey = key.size() >= 10
            && (key.ends_with("mqaencoder") || key.ends_with("mqaencoderversion"));
        if (encoderKey && isTruthyMqaMetadataValue(entry->value))
            return true;
    }
    return false;
}

inline bool hasMqaEncoderMetadata(const AVDictionary* containerMetadata, const AVDictionary* streamMetadata)
{
    return hasMqaEncoderMetadata(containerMetadata) || hasMqaEncoderMetadata(streamMetadata);
}

} // namespace echo
