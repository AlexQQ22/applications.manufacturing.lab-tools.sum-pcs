# Copy SystemUtilizationMonitor data files since last cutoff.
# Needs to run from DPC
# Sergio Salas sergio.salas@intel.com
use File::Copy;
use File::Compare;
use Time::Local;
use warnings;

# Paths and Variables
($day, $month, $year, $hours, $minutes, $seconds) = (localtime)[3,4,5,2,1,0];
$Today = timelocal(localtime());
$CT24h = sprintf("%02d%02d", $hours, $minutes);
$TodayTS=sprintf("%04d%02d%02d", $year+1900, $month+1, $day);
#print "This is TodayTS: $TodayTS\n";
$Yesterday = $Today - (86400);
#print "Today: $Today, Yesterday: $Yesterday\n";
($seconds, $minutes, $hours, $day, $month, $year)=localtime($Yesterday);
$YesterdayTS=sprintf("%04d%02d%02d", $year+1900, $month+1, $day);
#print "This is YesterdayTS: $YesterdayTS\n";
#print "This is CT24h: $CT24h\n";
$log_file = "C\:\\TEMP\\PullSUMLogs_".$TodayTS.".log";
WriteLog("CT24h: $CT24h TodayTS: $TodayTS YesterdayTS: $YesterdayTS");
$PCName=`hostname`;
chomp($PCName);
WriteLog("Running SUM on: $PCName");
print("Running SUM on: $PCName");

#@TestersHDMx10 = ("A101","A102");
@TestersHDMx10 = ("A101","A102","A201","A202","A301","A302","A401","A402","A501","A502");
@TestersHDMx30 = ("A101","A102","A201","A202","A301","A302","A401","A402","A501","A502","B101","B102","B201","B202","B301","B302","B401","B402","B501","B502","C101","C102","C201","C202","C301","C302","C401","C402","C501","C502"); @TestersHSTx24 = ("A101","A201","A301","A401","A501","A601","B101","B201","B301","B401","B501","B601","C101","C201","C301","C401","C501","C601","D101","D201","D301","D401","D501","D601");
# @TestersHSTx24 = ("A101");
@TestersSSTx20 = ("A101","A201","A301","A401","B101","B201","B301","B401","C101","C201","C301","C401","D101","D201","D301","D401","E101","E201","E301","E401");
@TestersPTCx5 = ("A101","A201","A301","A401","A501");
@TestersSECS =  ("A101","A201","A301","A401","B101","B201","B301","B401","C101","C201","C301","C401");
@TestersSAP =  ("A101","A201","A301","A401","A501","A601","B101","B201","B301","B401","B501","B601");

$IPPreffix = '10.250.0.';
$IPStart = 1;
if ($PCName=~/\w\w\d\dDHHX\d\d\d\d/i) { #CR03THHX1844
    @Positions = @TestersHDMx10;
    print "This is a HDMx X10 module...\n";
    $Type="HDMX";
} elsif ($PCName=~/\w\w\d\dDHTX\d\d\d\d/i) { 
    @Positions = @TestersHDMx30;
    print "This is a HDMx X30 module...\n";
    $Type="HDMX";
} elsif ($PCName=~/\w\w\d\dDHST\d\d\d\d/i) { #CR03THST4471  CR03DHST4467
    @Positions = @TestersHSTx24;
    print "This is a HST X24 module...\n";
    $Type="HST";
} elsif ($PCName=~/\w\w\d\d(H|D)PTC\d\d\d\d/i) { #CR03HPTC5155 CR03DPTC4560
    @Positions = @TestersPTCx5;
    print "This is a PTC X5 module...\n";
    $Type="PTC";
} elsif ($PCName=~/\w\w\d\dTSST\d\d\d\d/i) { #CR03TSST0009
    @Positions = @TestersSSTx20;
    print "This is a SST X24 module...\n";
    $Type="SST";
} elsif ($PCName=~/\w\w\d\d(TPBT|TPPV)\d\d\d\dE*N*/i) { # CR03TPPV0083 CR03TPPV0083EN CR03TPBT0001
    @Positions = @TestersSECS;
    print "This is a SECS module...\n";
    $Type="SECS";
} elsif ($PCName=~/\w\w\d\dTASP\d\d\d\d/i) { #PG12TASP0001
    @Positions = @TestersSAP;
    print "This is a SAP X12 module...\n";
    $Type="SAP";
} else {
    print "This script should run only on DPC PCs for HDMx, HST, PTC, SECS, SST... exiting\n";
    WriteLog("PC type not supported, exiting!");
    exit;
};
WriteLog("PC type $Type selected for $PCName");
#Modify the 2 lines below to match your install location and json file depository
$DestinationDirectory = "\\\\amr\.corp\.intel\.com\\ec\\proj\\mdl\\cr\\intel\\hdmx_db\\mae\\SUM\\";
$SUMSourceDirectory = "\\\\amr\.corp\.intel\.com\\ec\\proj\\mdl\\cr\\intel\\hdmx_db\\mae\\Releases\\SystemUtilization";
$DestinationDirectory = $DestinationDirectory.$Type."\\";
#Copy SUM files
print "Copying and veryfying SUM installation files...\n";
$perm ="0770";

# #Start New code



 # system ("rmdir c:\\SUMInstall /s /q");


# #End New code

$TriggerInstallDPC=0;
$TriggerInstall=0;
	
	
$RevPath = $SUMSourceDirectory."\\Tester\\Rev.txt";
$TypeJSON=$SUMSourceDirectory."\\JSONConfig\\".$Type."appsettings.json";


if (!-e "c:\\SUMInstall") {
    mkdir "c:\\SUMInstall", $perm;
	mkdir "c:\\SUMInstall\\MonitoringSUM", $perm;
	mkdir "c:\\SUMInstall\\MonitoringSUM\\Release", $perm;
	copy ("$SUMSourceDirectory\\SystemUtilization.Monitor.Installer.msi", "c:\\SUMInstall");
	copy ("$SUMSourceDirectory\\CreateTaskSUM.bat", "c:\\SUMInstall");
	copy ("$SUMSourceDirectory\\CreateTaskSUM.ps1", "c:\\SUMInstall");
	copy ("$SUMSourceDirectory\\PullSUMLogs.vbs", "c:\\SUMInstall");
	copy ($TypeJSON,"C:\\SUMInstall\\appsettings.json");
	CopyDirectory("$SUMSourceDirectory\\PSExec","C:\\SUMInstall");
    CopyDirectory("$SUMSourceDirectory\\Tester","C:\\SUMInstall");
	CopyDirectory("$SUMSourceDirectory\\MonitoringSUM","C:\\SUMInstall\\MonitoringSUM");
	CopyDirectory("$SUMSourceDirectory\\MonitoringSUM\\Release","C:\\SUMInstall\\MonitoringSUM\\Release");
	WriteLog("c:\\SUMInstall created");
	$TriggerInstallDPC=1;
	print("c:\\SUMInstall created");

};


if (!-e "c:\\SUMInstall\\MonitoringSUM") {
    mkdir "c:\\SUMInstall\\MonitoringSUM", $perm;
	CopyDirectory("$SUMSourceDirectory\\MonitoringSUM","C:\\SUMInstall\\MonitoringSUM");
	$TriggerInstallDPC=1;
    print("c:\\SUMInstall\\MonitoringSUM");
};

if (!-e "c:\\SUMInstall\\MonitoringSUM\\Release") {
    mkdir "c:\\SUMInstall\\MonitoringSUM\\Release", $perm;
	CopyDirectory("$SUMSourceDirectory\\MonitoringSUM\\Release","C:\\SUMInstall\\MonitoringSUM\\Release");
	$TriggerInstallDPC=1;
     print("c:\\SUMInstall\\MonitoringSUM\\Release");
};




if ( (!-r"c:\\SUMInstall\\Rev.txt") ||  ( compare("c:\\SUMInstall\\Rev.txt","$RevPath" ) ne 0)) {  #check if filename changes are detected or not
    print( "Rev is dferent between the DPC and i source \n \n");
	WriteLog("Rev.txt does not match, triggers will be execute...");
	system ("rmdir c:\\SUMInstall /s /q");
	sleep 1;
	mkdir "c:\\SUMInstall", $perm;
	mkdir "c:\\SUMInstall\\MonitoringSUM", $perm;
	mkdir "c:\\SUMInstall\\MonitoringSUM\\Release", $perm;
	copy ("$SUMSourceDirectory\\SystemUtilization.Monitor.Installer.msi", "c:\\SUMInstall");
	copy ("$SUMSourceDirectory\\CreateTaskSUM.bat", "c:\\SUMInstall");
	copy ("$SUMSourceDirectory\\CreateTaskSUM.ps1", "c:\\SUMInstall");
	copy ("$SUMSourceDirectory\\PullSUMLogs.vbs", "c:\\SUMInstall");
	copy ($TypeJSON,"C:\\SUMInstall\\appsettings.json");
	CopyDirectory("$SUMSourceDirectory\\MonitoringSUM\\Release","C:\\SUMInstall\\MonitoringSUM\\Release"); 
	CopyDirectory("$SUMSourceDirectory\\MonitoringSUM","C:\\SUMInstall\\MonitoringSUM");
    CopyDirectory("$SUMSourceDirectory\\PSExec","C:\\SUMInstall");
    CopyDirectory("$SUMSourceDirectory\\Tester","C:\\SUMInstall");
    $TriggerInstallDPC=1;
}
else
{
   print( "Rev.txt is equal between the DPC and i source \n \n");
   WriteLog("Rev.txt is equal between the DPC and i source");
};



#Check NBTSTAT EXECUTABLE PATH
#if (-e "c:\\Windows\\sysnative\\nbtstat.exe") {$SystemDir = "sysnative"} else {$SystemDir ="system32"};
#WriteLog("SystemDir is: $SystemDir");
#********** MAIN SCRIPT **********
WriteLog("Started");
print("Started \n");


foreach $Position (@Positions) {

	$IPAddress = $IPPreffix.$IPStart;
    print "START VERIFICATION OF SYSTEM UTILIZATION MONITOR FROM: $IPAddress \n";
	
    $Ping=`ping -n 1 $IPAddress`;
	
	
	if ($Ping=~/Reply from /g) {
        $OnLine="1";
		print("Host with IP $IPAddress is online \n");
        WriteLog ("Host with IP $IPAddress is online");
    } else {
        $OnLine="0";
        WriteLog ("Host with IP $IPAddress is not online");
		print ("Host with IP $IPAddress is not online \n");
    };
	
	
	 if ($OnLine eq "1") {
		 
		$SourceDrive= "\\\\".$IPAddress."\\c\$";
        $NetUseCmd= "net use ".$SourceDrive.' /user:'.$IPAddress.'\SysC tr@nsf3r';
        $NetUseDeleteCmd= "net use ".$SourceDrive.' /delete';
        $status = system($NetUseCmd);
        WriteLog("Drive Map status for $IPAddress is: $status");
		print "\Drive Map status for $IPAddress is: $status \n";
        #Find out user directory for SysC and hostname for tester
        $FindSourceDirComputername=`C:\\SUMInstall\\PsExec64.exe \\\\$IPAddress -u SysC -p tr\@nsf3r cmd \/c echo #^%USERPROFILE^%#^%COMPUTERNAME^%#`;
		$Hostname = "";
		print "$FindSourceDirComputername \n";
        if ( ($FindSourceDirComputername=~/\#C:(\\Users\\\w+)\#(\w+)\#/i) or  ($FindSourceDirComputername=~/\#C:(\\Users\\\w+)\#(\w+)-(\w)\#/i)  ) {
            $UserDir=$1;
            $Hostname=$2;
            WriteLog ("Tester with IP $IPAddress has Hostname: $Hostname  and UserDir: $UserDir");
            print "Tester with IP $IPAddress has Hostname: $Hostname  and UserDir: $UserDir\n";
        };
		
        #Check if tester SUMInstall Exists
        if (-d "$SourceDrive\\SUMInstall") {
		     print("Tester with IP $IPAddress  has the file SUMInstall,will check if the SUM is running \n");
		     $sumcheckrunningcommand = "c:\\SUMInstall\\checkRunning_toTester.bat ".$IPAddress."\n";
			
			 print"$sumcheckrunningcommand";
             system ($sumcheckrunningcommand);
			
		     $sourcePathRev = $SourceDrive."\\SUMInstall\\Rev.txt";
			 print "$sourcePathRev";
			if((!-e  $sourcePathRev ) ||  compare($sourcePathRev, "c:\\SUMInstall\\Rev.txt") ne 0)
			{
				print("Tester with IP $IPAddress , has  a other version of the REV or y wasn't running the SUM \n");
				 $TriggerInstall=1;
				  push(@Online,$IPAddress);
			}
			else{
			
			    print("Tester with IP $IPAddress , has  the same  version of the REV \n");
			}
			
        } else {
            WriteLog("$SourceDrive.\\SUMInstall does not exist,the triggering install will be launch...");
			print "$SourceDrive.\\SUMInstall does not exist into the tester,the triggering install will be launch \n";
			$TriggerInstall=1;
			 push(@Online,$IPAddress);
        };
		 
		 
		 
		 
      #Check if tester current JSON configuration file is valid
        
        `title ******* SUM LOG COPY - DO NOT CLOSE THIS WINDOW *******`;
		if($Hostname ne ""){
			
			$SourceDirectory=$SourceDrive.$UserDir."\\AppData\\Local\\Intel\\SystemUtilizationMonitor";
			if (-e $SourceDirectory) {
				WriteLog ("Checking $SourceDirectory for json files...");
				opendir(DIR, $SourceDirectory);
				@JSONfiles = grep { /SystemUtilizationTimeFrames.*json$/ } readdir(DIR);
				if (@JSONfiles) {
					foreach $JSONfile (@JSONfiles) {
						#SystemUtilizationTimeFrames20240421.json
						if ($JSONfile=~/(SystemUtilizationTimeFrames\d+)\.json/) { 
							$BaseJSONName=$1;
							#FIND OUT WHICH FILE AND COPY RENAMED
							if ($BaseJSONName=~/\w+($TodayTS)$/) {
								$NewJSONName= $BaseJSONName."_".$PCName."_(".$Hostname.")_".$Position.".json";
								WriteLog("Copying file: $JSONfile");
								copy ("$SourceDirectory\\$JSONfile", "$DestinationDirectory\\$NewJSONName") or WriteLog("Copy failed: $!");
							};
							#SELECT PREVIOUS DAY FILE IF TIME BETWEEN 00:00 - 02:00
							# DO TIME STUFF
							if ($BaseJSONName=~/\w+($YesterdayTS)$/) {
								if ($CT24h >= 0000 and $CT24h <= 0100) {
									$NewJSONName= $BaseJSONName."_".$PCName."_(".$Hostname.")_".$Position.".json";
									WriteLog("It is between 00:00 and 01:00, copying previous day file: $JSONfile");
									copy ("$SourceDirectory\\$JSONfile", "$DestinationDirectory\\$NewJSONName") or WriteLog("Copy failed: $!");
								};
							};
						};
					};
				} else {
					WriteLog("No *.JSON files found in $SourceDirectory...");
					$InstallCommand = "c:\\SUMInstall\\InstallSUM_toTester.bat ".$IPAddress."\n";
					system ($InstallCommand);
				};
				closedir(DIR);
			} else {
				 print "$SourceDirectory does not exist, installing SUM in $Hostname with IP: $IPAddress...\n";
				 WriteLog("$SourceDirectory does not exist, installing SUM in $Hostname with IP: $IPAddress...");
				 $InstallCommand = "c:\\SUMInstall\\InstallSUM_toTester.bat ".$IPAddress."\n";
				 system ($InstallCommand); 
			};
			
		}
		else{
		   WriteLog("scritp can read the hostname from the source: $SourceDrive ");
		   print "scritp can read the hostname from the source: $SourceDrive ";
		}

        $status = system($NetUseDeleteCmd);
        WriteLog ("Delete mapped drive status for $IPAddress is:$status");
        # push(@Online,$IPAddress); 
		 
		 
		 
		 
	 }
	
	
	print "END VERIFICATION OF SYSTEM UTILIZATION MONITOR FROM: $IPAddress \n \n";
	
	$IPStart++;
}





print "value of TriggerInstallDPC: $TriggerInstallDPC ";
print "value of TriggerInstall: $TriggerInstall ";



if ($TriggerInstall) {
	print("WE ARE INTO THE TRIGGER INSTALL\n");

	my $Tester;
    foreach $Tester (@Online) {
				
        WriteLog("Reinstallation triggered due to new MSI for IPaddress: $Tester");
        $SUMInstallCommand = "c:\\SUMInstall\\InstallSUM_toTester.bat ".$Tester."\n";
        system ($SUMInstallCommand);
        $MSIatTester = "\\\\".$Tester."\\c\$\\SUMInstall\\SystemUtilization.Monitor.Installer.msi";
        if (compare("c:\\SUMInstall\\SystemUtilization.Monitor.Installer.msi", "$MSIatTester") ne 0) {
            WriteLog("At $Tester c:\\SUMInstall\\SystemUtilization.Monitor.Installer.msi was not updated successfully");
        };
    
    };
		
};

WriteLog("TASK CREATED INTO THE DPC\n");
if ($TriggerInstallDPC or $TriggerInstall) {
	# print("TASK CREATED INTO THE DPC\n");
	$SUMInstallCommandDPC = "c:\\SUMInstall\\CreateTaskSUM.bat";
	system ($SUMInstallCommandDPC);
	
};


PurgeLogs("c:\\TEMP","PullSU*\.log*");
WriteLog("Ended");
# sleep 60;




#********* SUBROUTINES ***********

sub PrintArray {
print join("\n",@_),"\n";
}
sub WriteLog {
my ($ToWrite);
$ToWrite=@_;
if (-e "$log_file") {
  open(LogFile,">>$log_file") or die "Failed to open log file $log_file: $!";
      }
else {
  open(LogFile,">$log_file") or die "Failed to open log file $log_file: $!";
  }
chomp($ToWrite);
$log_now = localtime;
print LogFile "$log_now @_\n";
close(LogFile);
}

sub FindFiles {
my $FileExpr=$_[0];
my $FilePath=$_[1];
my @GetNamesArray;
  open(DOSDIR,"dir $FilePath /B /O:-D /A:-D /T:C |"); #only files (hidden,system,archive), sorted by creation date descending 
  while (<DOSDIR>) {
    chomp;
    $PathFile=$FilePath."/".$_;
    #print "$PathFile\n";
      if ( /$FileExpr/) {
        @GetNamesArray = (@GetNamesArray,$PathFile);
      }
  }
  if(exists($GetNamesArray[0])) { 
    return @GetNamesArray
  } else {
    return 0;  
  };
}

sub CheckDate {
my @DatetoCheck=@_;
my $Result;
my $DatetoCheckET;
my $FiveDaysAgoET;
    $DatetoCheckET = timelocal(@DatetoCheck);
    #print "These are epoch seconds $DatetoCheckET\n";
if ($DatetoCheckET > $FiveDaysAgoET ) {
    #print "Valid $DatetoCheck ET: $DatetoCheckET\n";
    $Result = 1;
    } else  {
    #print "Not Valid $DatetoCheck ET: $DatetoCheckET\n";
    $Result = 0;
    }   
return $Result;
}

sub CopyDirectory {
    my ($SourceDir,$DestinationDir) = @_;
    opendir(DIR, $SourceDir) or WriteLog("Can't opendir $SourceDir: $!");
    while (defined($file = readdir(DIR))) {
        copy ("$SourceDir\\$file", "$DestinationDir");
    }
    closedir(DIR);
}

sub PurgeLogs {
    my ($DirToPurge,$Wildcard)=@_;
    my $FileAgeLimit = 15;
    if (-d $DirToPurge) {
        WriteLog("Checking for files older than $FileAgeLimit days in $DirToPurge, with wildcard: $Wildcard");
        $GlobDir=$DirToPurge."\\".$Wildcard;
        @FilesToPurge = glob("$GlobDir");
        foreach $FileToPurge (@FilesToPurge){
            if ( -M $FileToPurge > $FileAgeLimit ) {
                @stat = stat($FileToPurge);
                $mtime = localtime $stat[9];
                $size = formatSize($stat[7]);
                unlink ($FileToPurge);
                WriteLog("Deleted $FileToPurge which is older than $FileAgeLimit days... Modified: $mtime Size: $size");
            };
        };
    } else {
    WriteLog(">>>>> The directory $DirToPurge does not exist!!!!!");
    };
}

sub formatSize {
    my $size = shift;
    my $exp = 0;
    $units = [qw(B KB MB GB TB PB)];
    for (@$units) {
        last if $size < 1024;
        $size /= 1024;
        $exp++;
    }
    return wantarray ? ($size, $units->[$exp]) : sprintf("%.2f %s", $size, $units->[$exp]);
}