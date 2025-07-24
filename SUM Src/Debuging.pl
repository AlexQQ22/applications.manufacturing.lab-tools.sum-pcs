print "Ingrese la ip del tester correr desde la DPC \n";

$IPAddress = <STDIN>;
chomp $IPAddress;
print "START VERIFICATION OF SYSTEM UTILIZATION MONITOR FROM: $IPAddress \n";

$Ping=`ping -n 1 $IPAddress`;


if ($Ping=~/Reply from /g) {
	$OnLine="1";
	print("Host with IP $IPAddress is online \n");
	
} else {
	$OnLine="0";
	print ("Host with IP $IPAddress is not online \n");
};

$SourceDrive= "\\\\".$IPAddress."\\c\$";
$NetUseCmd= "net use ".$SourceDrive.' /user:'.$IPAddress.'\SysC tr@nsf3r';
$NetUseDeleteCmd= "net use ".$SourceDrive.' /delete';
$status = system($NetUseCmd);

$FindSourceDirComputername=`C:\\SUMInstall\\PsExec64.exe \\\\$IPAddress -u SysC -p tr\@nsf3r cmd \/c echo #^%USERPROFILE^%#^%COMPUTERNAME^%#`;
$Hostname = "";
print "$FindSourceDirComputername \n";
if ( ($FindSourceDirComputername=~/\#C:(\\Users\\\w+)\#(\w+)\#/i) or  ($FindSourceDirComputername=~/\#C:(\\Users\\\w+)\#(\w+)-(\w)\#/i)  ) {
	$UserDir=$1;
	$Hostname=$2;
	print "Tester with IP $IPAddress has Hostname: $Hostname  and UserDir: $UserDir\n";
}
else{
	print "no se leyo el hostname";
	
};





# $status = system($NetUseDeleteCmd);
 
 sleep (45);