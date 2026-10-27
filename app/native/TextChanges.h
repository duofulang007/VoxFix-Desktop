#pragma once
#include <string>
#include <vector>
#include <stdint.h>
#include <algorithm>

struct TextChange { size_t start, end; std::wstring text; };
struct TextPoints {
    std::vector<uint32_t> values;
    std::vector<size_t> offsets;
    bool Read(const std::wstring& text) {
        for(size_t i=0;i<text.size();++i){
            offsets.push_back(i);uint32_t point=text[i];
            if(point>=0xd800 && point<=0xdbff){
                if(i+1>=text.size() || text[i+1]<0xdc00 || text[i+1]>0xdfff)return false;
                point=0x10000+((point-0xd800)<<10)+(text[++i]-0xdc00);
            }else if(point>=0xdc00 && point<=0xdfff)return false;
            values.push_back(point);
        }
        offsets.push_back(text.size());return true;
    }
};

// Changes are returned from right to left, so earlier offsets survive every edit.
inline bool TextChanges(const std::wstring& previous,const std::wstring& text,std::vector<TextChange>& changes) {
    TextPoints oldPoints,newPoints;if(!oldPoints.Read(previous) || !newPoints.Read(text))return false;
    if(oldPoints.values.size()>8192 || newPoints.values.size()>8192)return false;
    size_t prefix=0,suffix=0;
    while(prefix<oldPoints.values.size() && prefix<newPoints.values.size() && oldPoints.values[prefix]==newPoints.values[prefix])++prefix;
    while(suffix<oldPoints.values.size()-prefix && suffix<newPoints.values.size()-prefix && oldPoints.values[oldPoints.values.size()-1-suffix]==newPoints.values[newPoints.values.size()-1-suffix])++suffix;
    size_t n=oldPoints.values.size()-prefix-suffix,m=newPoints.values.size()-prefix-suffix;
    if(!n && !m)return true;
    // shortcut: cap one segment's matrix at 1M cells; use a linear-space diff for larger segments.
    if((n+1)*(m+1)>1048576)return false;
    std::vector<uint16_t> costs((n+1)*(m+1));
    auto cell=[&](size_t i,size_t j)->uint16_t&{return costs[i*(m+1)+j];};
    for(size_t i=0;i<=n;++i)cell(i,0)=static_cast<uint16_t>(i);
    for(size_t j=0;j<=m;++j)cell(0,j)=static_cast<uint16_t>(j);
    for(size_t i=1;i<=n;++i)for(size_t j=1;j<=m;++j){
        uint16_t diagonal=static_cast<uint16_t>(cell(i-1,j-1)+(oldPoints.values[prefix+i-1]==newPoints.values[prefix+j-1]?0:1));
        cell(i,j)=(std::min)(diagonal,static_cast<uint16_t>(1+(std::min)(cell(i-1,j),cell(i,j-1))));
    }
    size_t i=n,j=m,endI=i,endJ=j;bool changing=false;
    auto flush=[&](){
        if(changing){size_t from=newPoints.offsets[prefix+j],to=newPoints.offsets[prefix+endJ];changes.push_back({oldPoints.offsets[prefix+i],oldPoints.offsets[prefix+endI],text.substr(from,to-from)});changing=false;}
    };
    while(i || j){
        if(i && j && oldPoints.values[prefix+i-1]==newPoints.values[prefix+j-1] && cell(i,j)==cell(i-1,j-1)){
            flush();--i;--j;continue;
        }
        if(!changing){endI=i;endJ=j;changing=true;}
        if(i && j && cell(i,j)==cell(i-1,j-1)+1){--i;--j;}
        else if(i && cell(i,j)==cell(i-1,j)+1)--i;
        else if(j)--j;
        else return false;
    }
    flush();return true;
}
