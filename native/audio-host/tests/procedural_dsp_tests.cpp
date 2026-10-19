#include "ProceduralDsp.h"
#include <cstdlib>
#include <iostream>
#include <stdexcept>
#include <new>
static bool watchAllocations = false;
static size_t allocations = 0;
void* operator new(std::size_t n) { if (watchAllocations) ++allocations; if (auto* p = std::malloc(n)) return p; throw std::bad_alloc(); }
void operator delete(void* p) noexcept { std::free(p); }
void* operator new[](std::size_t n) { return ::operator new(n); }
void operator delete[](void* p) noexcept { ::operator delete(p); }
namespace {
using Json = nlohmann::json;
void check(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
Json step(const char* kind, const char* id, Json channels = {1,2}) {
    return Json{{"kind",kind},{"id",id},{"name",""},{"enabled",true},{"channels",channels},{"gainDb",0},{"delayMs",0},
      {"bands",Json::array()},{"routes",Json::array()}, {"coefficients",{{"b0",1},{"b1",0},{"b2",0},{"a1",0},{"a2",0}}}};
}
void publish(echo::ProceduralDspController& c, Json steps) {
    std::string error; check(c.setState(Json{{"enabled",true},{"operations",steps}}, error), "valid graph rejected");
}
void fill(echo::FloatAudioBuffer& b, float value) { for (int c=0;c<b.getNumChannels();++c) for(int s=0;s<b.getNumSamples();++s) b.getWritePointer(c)[s]=value; }
void testIndependentEqualizers() {
    echo::ProceduralDspController c;
    auto first=step("eq","a",{9,32}), second=step("eq","b",{32});
    first["bands"]={Json{{"enabled",true},{"filterType","peaking"},{"frequencyHz",1000},{"gainDb",6},{"q",1}}};
    second["bands"]=first["bands"];
    publish(c,{first,second}); echo::ProceduralDspProcessor p(c); p.prepare(48000,32);
    echo::FloatAudioBuffer b(32,4800);
    for(int ch=0;ch<32;++ch) for(int i=0;i<4800;++i) b.getWritePointer(ch)[i]=static_cast<float>(0.01*std::sin(2*3.141592653589793*1000*i/48000));
    p.processBlock(b,0,4800);
    double ref=0,one=0,two=0;
    for(int i=2400;i<4800;++i) { ref+=std::pow(b.getReadPointer(0)[i],2); one+=std::pow(b.getReadPointer(8)[i],2); two+=std::pow(b.getReadPointer(31)[i],2); }
    check(std::abs(10*std::log10(one/ref)-6)<0.02,"channel 9 EQ gain incorrect");
    check(std::abs(10*std::log10(two/ref)-12)<0.02,"multiple EQ groups did not cascade on channel 32");
}
void testRoutingAndOrder() {
    echo::ProceduralDspController c;
    auto mix=step("mix","mix",{1,32}), invert=step("invert","invert",{32});
    mix["routes"]={Json{{"input",32},{"output",1},{"gainDb",0},{"invert",false}},Json{{"input",1},{"output",32},{"gainDb",0},{"invert",false}},Json{{"input",2},{"output",1},{"gainDb",0},{"invert",true}}};
    publish(c,{mix,invert}); echo::ProceduralDspProcessor p(c); p.prepare(48000,32);
    echo::FloatAudioBuffer b(32,1); b.getWritePointer(0)[0]=0.2f; b.getWritePointer(1)[0]=0.1f; b.getWritePointer(31)[0]=0.5f;
    p.processBlock(b,0,1);
    check(std::abs(b.getReadPointer(0)[0]-0.4f)<1.e-6,"mix summation incorrect");
    check(std::abs(b.getReadPointer(31)[0]+0.2f)<1.e-6,"mix fed back or operation order incorrect");
    check(b.getReadPointer(1)[0]==0.1f,"unselected channel mutated");
}
void testDelayResetAndTransition() {
    echo::ProceduralDspController c;
    auto delay=step("delay","delay",{32}); delay["delayMs"]=1;
    publish(c,{delay}); echo::ProceduralDspProcessor p(c); p.prepare(48000,32);
    echo::FloatAudioBuffer b(32,64); b.getWritePointer(31)[0]=0.5f;
    p.processBlock(b,0,1); check(p.tailFramesRemaining()==48,"delay tail missing");
    p.processBlock(b,1,63); check(b.getReadPointer(31)[48]==0.5f,"channel 32 delay incorrect");
    p.reset(); b.clear(); p.processBlock(b,0,64);
    check(b.getReadPointer(31)[48]==0,"hard replacement replayed old delay history");
    fill(b,0.25f); p.processBlock(b,0,64);
    auto mute=step("mute","mute",{32}); publish(c,{mute}); fill(b,0.25f);
    watchAllocations=true; p.processBlock(b,0,64); watchAllocations=false;
    check(allocations==0,"graph adoption allocated on audio thread");
    check(b.getReadPointer(31)[0]>0 && b.getReadPointer(31)[63]>0,"transition stepped immediately");
    for(int i=0;i<4;++i) {fill(b,0.25f);p.processBlock(b,0,64);}
    check(b.getReadPointer(31)[63]==0 && b.getReadPointer(0)[63]==0.25f,"mute affected wrong channels");
    std::string error; check(c.setState(Json{{"enabled",false},{"operations",{mute}}},error),"bypass rejected");
    for(int i=0;i<5;++i) {fill(b,0.25f);p.processBlock(b,0,64);}
    check(b.getReadPointer(31)[63]==0.25f && !p.isActive(),"bypass did not return dry signal");
}
void testValidationAndIir() {
    echo::ProceduralDspController c; auto iir=step("iir","iir",{1}); iir["coefficients"]["b0"]=0.5; publish(c,{iir});
    const auto saved=c.getState(); iir["coefficients"]["a2"]=1;
    std::string error; check(!c.setState(Json{{"enabled",true},{"operations",{iir}}},error),"unstable IIR accepted");
    check(c.getState()==saved,"failed publication corrupted last graph");
    echo::ProceduralDspProcessor p(c);p.prepare(48000,2);echo::FloatAudioBuffer b(2,1);fill(b,0.2f);p.processBlock(b,0,1);
    check(b.getReadPointer(0)[0]==0.1f && b.getReadPointer(1)[0]==0.2f,"IIR coefficient execution incorrect");
    auto delay=step("delay","d",{1,2,32});delay["delayMs"]=2000;delay["enabled"]=false;
    check(!c.setState(Json{{"enabled",true},{"operations",{delay}}},error),"delay budget bypassed");
}
}
int main() { try { testIndependentEqualizers();testRoutingAndOrder();testDelayResetAndTransition();testValidationAndIir();std::cout<<"procedural DSP: EQ cascade, 32 channels, routing, delay/tail/reset, crossfade, zero callback allocations, IIR and rejection passed\n";return 0; } catch(const std::exception& e) { std::cerr<<e.what()<<'\n';return 1; } }
