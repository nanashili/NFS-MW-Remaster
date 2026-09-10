// Material IDs sampled from the highest natural-ground triangle at pixel centres.
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <vector>
int main(int argc,char**argv){
 if(argc!=8)return 2;
 int w=std::stoi(argv[3]),h=std::stoi(argv[4]);double ox=std::stod(argv[5]),oz=std::stod(argv[6]),step=std::stod(argv[7]);
 std::vector<float> heights(size_t(w)*h,-INFINITY);std::vector<uint8_t> labels(size_t(w)*h,255);
 std::ifstream in(argv[1],std::ios::binary);if(!in)return 3;uint32_t label;float v[9];
 while(in.read(reinterpret_cast<char*>(&label),4)){
  if(!in.read(reinterpret_cast<char*>(v),36)||label>=8)return 4;
  double x[3],z[3];for(int i=0;i<3;i++){x[i]=(v[3*i]-ox)/step;z[i]=(v[3*i+2]-oz)/step;}
  double den=(z[1]-z[2])*(x[0]-x[2])+(x[2]-x[1])*(z[0]-z[2]);if(std::abs(den)<1e-8)continue;
  int xmin=std::max(0,int(std::ceil(*std::min_element(x,x+3)-1e-6))),xmax=std::min(w-1,int(std::floor(*std::max_element(x,x+3)+1e-6)));
  int zmin=std::max(0,int(std::ceil(*std::min_element(z,z+3)-1e-6))),zmax=std::min(h-1,int(std::floor(*std::max_element(z,z+3)+1e-6)));
  for(int iz=zmin;iz<=zmax;iz++)for(int ix=xmin;ix<=xmax;ix++){
   double a=((z[1]-z[2])*(ix-x[2])+(x[2]-x[1])*(iz-z[2]))/den,b=((z[2]-z[0])*(ix-x[2])+(x[0]-x[2])*(iz-z[2]))/den,c=1-a-b;
   if(a<-1e-7||b<-1e-7||c<-1e-7)continue;
   float y=float(a*v[1]+b*v[4]+c*v[7]);size_t k=size_t(iz)*w+ix;if(y>heights[k]){heights[k]=y;labels[k]=uint8_t(label);}
  }
 }
 std::ofstream out(argv[2],std::ios::binary);out.write(reinterpret_cast<char*>(labels.data()),labels.size());if(!out)return 5;
 std::cout<<"Rasterized "<<labels.size()<<" material samples\n";
}
