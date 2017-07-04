#
# spec file for package plasticscm-server
#
# Copyright (c) 2013 B1 Systems GmbH, Vohburg, Germany.
#
# All modifications and additions to the file contributed by third parties
# remain the property of their copyright owners, unless otherwise agreed
# upon. The license for this file, and modifications and additions to the
# file, is the same license as for the pristine package itself (unless the
# license for the pristine package is not an Open Source License, in which
# case the license is the MIT License). An "Open Source License" is a
# license that conforms to the Open Source Definition (Version 1.9)
# published by the Open Source Initiative.
#

%define _binaries_in_noarch_packages_terminate_build 0
%define debug_package %{nil}
%define __debug_package %{nil}
%define __debug_install_post %{nil}

Name:      plasticscm-certtools
Version:   3.12.0
Release:   1
Summary:   Certificate management tools.
License:   BSD
Url:       http://plasticscm.com/
Group:     System/Management
Source0:   plasticscm-certtools.tar.gz
Requires:  plasticscm-mono3-core = 3.0.3.1
Requires:  plasticscm-mono3-web = 3.0.3.1
BuildArch: noarch

Buildroot: %{_tmppath}/%{name}-%{version}-build

%description
Certificate management tools: certmgr, mozroots. Customized to
allow working in batch mode (no user interaction required).


%prep
%setup -q -n certtools

%build

%install
mkdir -p $RPM_BUILD_ROOT/opt/plasticscm5/certtools/
cp -a * $RPM_BUILD_ROOT/opt/plasticscm5/certtools/
chmod a+x $RPM_BUILD_ROOT/opt/plasticscm5/certtools/mozroots
chmod a+x $RPM_BUILD_ROOT/opt/plasticscm5/certtools/certmgr


%post
    /opt/plasticscm5/certtools/mozroots --import --machine --add-only
    /opt/plasticscm5/certtools/certmgr -ssl -m -y https://www.plasticscm.com/


%clean
rm -rf $RPM_BUILD_ROOT

%files
%defattr(-,root,root,-)
%dir /opt/plasticscm5/certtools
/opt/plasticscm5/certtools/certmgr
/opt/plasticscm5/certtools/certmgr.exe
/opt/plasticscm5/certtools/mozroots
/opt/plasticscm5/certtools/mozroots.exe

%attr(0755,root,root) /opt/plasticscm5/certtools/certmgr
%attr(0755,root,root) /opt/plasticscm5/certtools/mozroots


%changelog

