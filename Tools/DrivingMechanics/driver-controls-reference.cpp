// CC0: DoAutoReverse and DoAutoBraking below are verbatim from dbalatoni13/nfsmw
// 3bb4ab24df54840a290310fe6d249bc13abe8235, PInput.cpp (PS2 SLUS-21351).
// Minimal ordinary-driving interface substitutes surround the recovered functions.
// Compile with clang++ -std=c++17 -O0; stdout regenerates the checked-in CSV oracle.
#include <algorithm>
#include <cstdio>
#include <vector>
namespace UMath {
    float Ramp(float v, float lo, float hi) { return std::clamp((v-lo)/(hi-lo), 0.0f, 1.0f); }
    float Clamp(float v, float lo, float hi) { return std::clamp(v, lo, hi); }
    unsigned int Min(unsigned int a, unsigned int b) { return std::min(a, b); }
}
enum GearID { G_REVERSE=0, G_NEUTRAL=1, G_FIRST=2 };
struct ITransmission { int gear; int GetGear() { return gear; } void Shift(GearID g) { gear=g; } };
struct Vehicle {
    ITransmission transmission; float speed;
    bool QueryInterface(ITransmission** p) { *p=&transmission; return true; }
    float GetSpeed() { return speed; }
};
struct InputPlayer {
    Vehicle* mVehicle; float gas, brake;
    void SetControlGas(float x) { gas=x; }
    void SetControlBrake(float x) { brake=x; }
    void DoAutoReverse(float gas_input, float brake_input);
    void DoAutoBraking(float gas_input, float brake_input);
};
void InputPlayer::DoAutoReverse(const float gas_input, const float brake_input) {
    if (this->mVehicle == nullptr) {
        return;
    }
    ITransmission *itranny;
    if (!this->mVehicle->QueryInterface(&itranny)) {
        return;
    }
    float speed = this->mVehicle->GetSpeed();
    if (itranny->GetGear() != G_REVERSE) {
        if ((speed < 2.5f) && (brake_input > 0.0f) && gas_input == 0.0f) {
            itranny->Shift(G_REVERSE);
        }
    } else if ((speed > -5.0f) && ((brake_input == 0.0f) || (gas_input > 0.0f))) {
        itranny->Shift(G_FIRST);
    }
    if (itranny->GetGear() == G_REVERSE) {
        this->SetControlGas(brake_input);
        this->SetControlBrake(gas_input);
    }
}
void InputPlayer::DoAutoBraking(const float gas_input, const float brake_input) {
    ITransmission *itranny;
    if (!this->mVehicle->QueryInterface(&itranny) || (gas_input > 0.0f) || (brake_input > 0.0f)) {
        return;
    }
    const int current_gear = itranny->GetGear();
    float speed = this->mVehicle->GetSpeed();
    if (current_gear > G_REVERSE) {
        if (speed < 8.0f) {
            float brake_ratio = UMath::Ramp(speed, 0.0f, 8.0f);
            this->SetControlBrake(1.0f - brake_ratio * 0.75f);
        }
    } else {
        float brake_ratio = UMath::Ramp(-speed, 0.0f, 10.0f);
        this->SetControlBrake(1.0f - brake_ratio * 0.75f);
    }
}
// Verbatim UTIL_InterprolateIndex (Sim/Util.h) and GetBrakingTorque (EngineRacer.cpp), same commit.
inline unsigned int UTIL_InterprolateIndex(unsigned int last_index, float value, float limit_min, float limit_max, float &ratio) {
    float value_range = limit_max - limit_min;
    float value_offset = value - limit_min;
    ratio = last_index * value_offset / value_range;
    unsigned int index1 = static_cast<unsigned int>(ratio);
    ratio -= index1;
    return index1;
}
struct EngineInfo {
    std::vector<float> braking;
    unsigned int Num_ENGINE_BRAKING() const { return braking.size(); }
    float ENGINE_BRAKING(unsigned int i) const { return braking.at(i); }
    float IDLE() const { return 1000.f; }
    float MAX_RPM() const { return 9000.f; }
    float RED_LINE() const { return 7000.f; }
};
struct EngineRacer {
    EngineInfo mEngineInfo;
    float GetBrakingTorque(float engine_torque, float rpm) const;
};
float EngineRacer::GetBrakingTorque(float engine_torque, float rpm) const {
    float torque = engine_torque;
    unsigned int numpts = this->mEngineInfo.Num_ENGINE_BRAKING();
    if (numpts > 1) {
        float rpm_min = this->mEngineInfo.IDLE();
        float rpm_max = this->mEngineInfo.MAX_RPM();
        float ratio;
        unsigned int index =
            UTIL_InterprolateIndex(numpts - 1, UMath::Clamp(rpm, this->mEngineInfo.IDLE(), this->mEngineInfo.RED_LINE()), rpm_min, rpm_max, ratio);

        float base = this->mEngineInfo.ENGINE_BRAKING(index);
        unsigned int secondIndex = index + 1;
        float step = this->mEngineInfo.ENGINE_BRAKING(UMath::Min(numpts - 1, secondIndex));
        float load_pct = base + (step - base) * ratio;
        return -torque * UMath::Clamp(load_pct, 0.f, 1.f);
    } else {
        return -torque * this->mEngineInfo.ENGINE_BRAKING(0);
    }
}
int main(int argc, char**) {
    if (argc > 1) {
        std::vector<std::vector<float>> tables{{.25f}, {1.2f}, {.1f,.5f,.9f}, {-.5f,1.5f}};
        std::puts("table,torque,rpm,braking");
        for (unsigned int table=0; table<tables.size(); table++)
        for (float torque : {0.f,100.f,400.f})
        for (float rpm : {0.f,1000.f,3000.f,5000.f,7000.f,9000.f,20000.f}) {
            EngineRacer engine{{tables[table]}};
            std::printf("%u,%.9g,%.9g,%.9g\n",table,torque,rpm,engine.GetBrakingTorque(torque,rpm));
        }
        return 0;
    }
    std::puts("gear,speed,gas,brake,handbrake,finalGear,finalGas,finalBrake");
    for (int gear : {0,1,2,3})
    for (float speed : {-20.f,-10.f,-5.f,-4.999f,0.f,2.499f,2.5f,4.f,7.999f,8.f,10.f,20.f})
    for (float gas : {0.f,.001f,.25f,1.f})
    for (float brake : {0.f,.001f,.7f,1.f})
    for (int handbrake : {0,1}) {
        Vehicle vehicle{{gear},speed}; InputPlayer input{&vehicle,gas,brake};
        input.DoAutoReverse(gas,brake); input.DoAutoBraking(gas,brake);
        if (handbrake) input.SetControlBrake(0.f);
        std::printf("%d,%.9g,%.9g,%.9g,%d,%d,%.9g,%.9g\n", gear-1,speed,gas,brake,handbrake,vehicle.transmission.gear-1,input.gas,input.brake);
    }
}
