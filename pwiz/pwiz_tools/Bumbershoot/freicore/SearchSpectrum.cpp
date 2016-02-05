//
// $Id: SearchSpectrum.cpp 8634 2015-06-30 16:34:24Z chambm $
//
// Licensed under the Apache License, Version 2.0 (the "License"); 
// you may not use this file except in compliance with the License. 
// You may obtain a copy of the License at 
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, 
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. 
// See the License for the specific language governing permissions and 
// limitations under the License.
//
// The Original Code is the Bumbershoot core library.
//
// The Initial Developer of the Original Code is Matt Chambers.
//
// Copyright 2009 Vanderbilt University
//
// Contributor(s): Surendra Dasari
//

#include "stdafx.h"
#include "SearchResultSet.h"

using namespace freicore;

SearchScoreList BaseSearchResult::emptyScoreList;

namespace std
{
    ostream& operator<< ( ostream& o, const SearchScore& rhs )
    {
        return ( o << "(" << rhs.name << ":" << rhs.value << ")" );
    }
}