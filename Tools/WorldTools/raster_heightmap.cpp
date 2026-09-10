// Rasterize authored triangles at exact world-grid sample locations.
// Input: uint32 layer (0=natural,1=road), nine float32 Unity XYZ per triangle.
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <limits>
#include <vector>
int main(int argc,char**argv) {
    if(argc!=8)return 2;
    const int width=std::stoi(argv[3]),height=std::stoi(argv[4]);
    const double ox=std::stod(argv[5]),oz=std::stod(argv[6]),step=std::stod(argv[7]);
    const size_t size=size_t(width)*height;
    std::vector<float> nmin(size,INFINITY),nmax(size,-INFINITY),rmin(size,INFINITY),rmax(size,-INFINITY);
    std::ifstream input(argv[1],std::ios::binary);if(!input)return 3;
    uint32_t layer;float v[9];size_t triangles=0,vertical=0;
    while(input.read(reinterpret_cast<char*>(&layer),4)) {
        if(!input.read(reinterpret_cast<char*>(v),36)||layer>1)return 4;
        double x[3],z[3];for(int i=0;i<3;i++){x[i]=(v[i*3]-ox)/step;z[i]=(v[i*3+2]-oz)/step;}
        double den=(z[1]-z[2])*(x[0]-x[2])+(x[2]-x[1])*(z[0]-z[2]);triangles++;
        if(std::abs(den)<1e-8){vertical++;continue;}
        int xmin=std::max(0,int(std::ceil(*std::min_element(x,x+3)-1e-6))),xmax=std::min(width-1,int(std::floor(*std::max_element(x,x+3)+1e-6)));
        int zmin=std::max(0,int(std::ceil(*std::min_element(z,z+3)-1e-6))),zmax=std::min(height-1,int(std::floor(*std::max_element(z,z+3)+1e-6)));
        auto&lo=layer?rmin:nmin;auto&hi=layer?rmax:nmax;
        for(int iz=zmin;iz<=zmax;iz++)for(int ix=xmin;ix<=xmax;ix++) {
            double a=((z[1]-z[2])*(ix-x[2])+(x[2]-x[1])*(iz-z[2]))/den;
            double b=((z[2]-z[0])*(ix-x[2])+(x[0]-x[2])*(iz-z[2]))/den;double c=1-a-b;
            if(a<-1e-7||b<-1e-7||c<-1e-7)continue;
            float y=float(a*v[1]+b*v[4]+c*v[7]);size_t k=size_t(iz)*width+ix;lo[k]=std::min(lo[k],y);hi[k]=std::max(hi[k],y);
        }
    }
    std::ofstream heights(std::string(argv[2])+".f32",std::ios::binary),mask(std::string(argv[2])+".coverage",std::ios::binary),multi(std::string(argv[2])+".multilevel",std::ios::binary);
    size_t covered=0,natural=0,road=0,layered=0;float lowest=INFINITY,highest=-INFINITY;
    for(size_t k=0;k<size;k++) {
        uint8_t valid=std::isfinite(nmax[k])?1:(std::isfinite(rmin[k])?2:0);
        // Keep natural ground over tunnel roads. At road-only coordinates use the
        // lowest drivable surface so elevated decks do not replace lower roads.
        float y=valid==1?nmax[k]:(valid==2?rmin[k]:std::numeric_limits<float>::quiet_NaN());
        float lo=std::min(nmin[k],rmin[k]),hi=std::max(nmax[k],rmax[k]);uint8_t conflict=valid&&hi-lo>0.5f;
        heights.write(reinterpret_cast<char*>(&y),4);mask.put(valid);multi.put(conflict);
        if(valid){covered++;natural+=valid==1;road+=valid==2;lowest=std::min(lowest,y);highest=std::max(highest,y);}layered+=conflict;
    }
    std::cout<<"{\"triangles\":"<<triangles<<",\"verticalOrDegenerate\":"<<vertical<<",\"samples\":"<<size<<",\"coveredSamples\":"<<covered<<",\"naturalSamples\":"<<natural<<",\"roadOnlySamples\":"<<road<<",\"multiLevelSamples\":"<<layered<<",\"heightMin\":"<<lowest<<",\"heightMax\":"<<highest<<"}\n";
}
