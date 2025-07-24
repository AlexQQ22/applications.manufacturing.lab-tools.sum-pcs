@ECHO OFF
REM title ******* SUM LOG COPY - DO NOT CLOSE THIS WINDOW *******
title SUM_cmd
c:
cd c:\TEMP
PATH C:\Strawberry\c\bin;C:\Strawberry\perl\site\bin;C:\Strawberry\perl\bin;%PATH%
ECHO ************** DO NOT CLOSE THIS WINDOW ******************
REM Install Perl if not found
perl -v
IF ERRORLEVEL 9009 (
    COPY /Y \\amr\ec\proj\mdl\cr\intel\hdmx_db\mae\Releases\SystemUtilization\strawberry-perl-5.32.1.1-64bit.msi c:\TEMP
    c:\temp\strawberry-perl-5.32.1.1-64bit.msi /quiet /norestart
)
ECHO ***** Performing System Utilization Monitor Log Copy *****
perl \\amr\ec\proj\mdl\cr\intel\hdmx_db\mae\Releases\SystemUtilization\PullSUMLogs.pl
ECHO  ***** Completed System Utilization Monitor Log Copy *****