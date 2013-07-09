cloudscript hadoop_single_node
    version             = _latest  
    result_template     = hadoop_result_template

globals
    server_password	    = lib::random_password()
    console_password    = lib::random_password()
    hadoop_slice_user   = 'hadoop'
    
thread hadoop_setup
    tasks               = [hadoop_node_setup]

task hadoop_node_setup

    #-------------------------------
    # create hadoop keys
    #-------------------------------
    
    # create hadoop server root password key
    /key/password hadoop_server_password_key read_or_create
        key_group       = _SERVER
        password        = server_password
    
    # create hadoop server console key
    /key/password hadoop_server_console_key read_or_create
        key_group       = _CONSOLE
        password        = console_password
        
    # create storage slice keys
    /key/token hadoop_slice_key read_or_create
        username        = hadoop_slice_user  
        
    #-------------------------------
    # create hadoop bootstrap 
    # script and recipe
    #-------------------------------
    
    # create slice to store script in cloudstorage
    /storage/slice hadoop_slice read_or_create
        keys            = [hadoop_slice_key]
    
    # create slice container to store script in cloudstorage
    /storage/container hadoop_container => [hadoop_slice] read_or_create
        slice               = hadoop_slice
    
    # place script data in cloudstorage
    /storage/object hadoop_bootstrap_object => [hadoop_slice, hadoop_container] read_or_create
        container_name  = 'hadoop_container'
        file_name       = 'bootstrap_hadoop.sh'
        slice           = hadoop_slice
        content_data    = hadoop_bootstrap_data
        
    # associate the cloudstorage object with the hadoop script
    /orchestration/script hadoop_bootstrap_script => [hadoop_slice, hadoop_container, hadoop_bootstrap_object] read_or_create
        data_uri        = 'cloudstorage://hadoop_slice/hadoop_container/bootstrap_hadoop.sh'
        script_type     = _SHELL
        encoding        = _STORAGE
    
    # create the recipe and associate the script
    /orchestration/recipe hadoop_bootstrap_recipe read_or_create
        scripts         = [hadoop_bootstrap_script]

    #-------------------------------
    # create the hadoop node
    #-------------------------------
    
    /server/cloud hadoop_server read_or_create
        hostname        = 'hadoop'
        image           = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type    = 'CS2.1'
        keys            = [hadoop_server_password_key, hadoop_server_console_key]
        recipes         = [hadoop_bootstrap_recipe]

text_template hadoop_bootstrap_data
#!/bin/sh

#-------------------------------
# Verify the pre-requisites 
# before installing Hadoop.
#-------------------------------

test `whoami` = 'root' || echo "You must be root to execute the commands."

# RET_CODE used to verify hardware and OS requirements all at once instead of piecemeal
RET_CODE=0

# supported ubuntu versions are: Squeeze Lenny Lucid Maverick
export RELEASE="`lsb_release -c | cut -f2`"
case $RELEASE in 
   ( "squeeze" | "lenny" | "lucid" | "maverick" ) 
      echo "OK: os version is supported by hadoop" 
      ;; 
   (*) 
      echo "ERROR: Hadoop is supported on ubuntu versions : Squeeze Lenny Lucid Maverick"
      RET_CODE=1
      ;; 
esac

R=`grep MemTotal /proc/meminfo | awk '{print $2}'`
if [ $R -gt 2000000 ]; then
   echo "OK: Server has more than the minimum of 2GB RAM"
else
   echo "ERROR: Server does not have enough memory.  A minimum of 2GB RAM is necessary"
   RET_CODE=1
fi

if [ $RET_CODE -ne 0 ]; then 
   echo "ERROR: Unable to proceed due to prevous errors.  Correct and re-run"
   exit 1
fi

#-------------------------------
# Install all the dependencies 
# and tools for Hadoop as a 
# single node. 
#-------------------------------

dpkg -s python-software-properties > /dev/null
if [ $? -ne 0 ]; then 
   apt-get update > /dev/null
   if [ $? -ne 0 ]; then 
      echo "ERROR: apt-get update failed to run after check of python-software-properties"
      exit 1
   else
      echo "OK: apt-get update python-software-properties"
   fi

   apt-get install -y python-software-properties > /dev/null
   if [ $? -ne 0 ]; then 
      echo "ERROR: apt-get install of python-software-properties failed to run."
      exit 1
   else
      echo "OK: apt-get install python-software-properties"
   fi
fi

grep "deb http://archive.canonical.com/ ${RELEASE} partner" /etc/apt/sources.list  >/dev/null
if [ $? -eq 1 ]; then 
   add-apt-repository "deb http://archive.canonical.com/ ${RELEASE} partner"
   if [ $? -ne 0 ]; then 
      echo "ERROR: add-apt-repository failed to run."
      exit 1
   else
      echo "OK: add-apt-repository"
   fi
else
   echo "OK: canonical partner in repository"
fi

apt-get update > /dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: apt-get update failed to run after addition of 'partner' repository."
   exit 1
else
      echo "OK: add-apt update partner"
fi

# Sun JDK install - Accept and avoid prompt to accept the license
# echo 'sun-java6-jdk shared/accepted-sun-dlj-v1-1 select true' | /usr/bin/debconf-set-selections
# echo 'sun-java6-jre shared/accepted-sun-dlj-v1-1 select true' | /usr/bin/debconf-set-selections

apt-get install -y openjdk-6-jre openjdk-6-jdk >/dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: apt-get install of the openjdk failed to run."
   exit 1
else
   echo "OK: openjdk java installed"
fi

# Update the Java system default to the Sun version
# Ubuntu allows multiple JVMs to be installed at any one time. To switch the "default" to the Sun JVM do the following.
update-java-alternatives -v -s java-6-openjdk >/dev/null
if [ $? -ne 0 ]; then 
   echo "INFO: update-java-alternative N/A."
else
   echo "OK: update-java-alternative."
fi

# Hadoop install Ubuntu
grep "deb http://archive.cloudera.com/debian ${RELEASE}-cdh3 contrib" /etc/apt/sources.list  >/dev/null
if [ $? -eq 1 ]; then 
   add-apt-repository "deb http://archive.cloudera.com/debian ${RELEASE}-cdh3 contrib"
   if [ $? -ne 0 ]; then 
      echo "ERROR: add-apt-repository failed to run for deb cloudera."
      exit 1
   else
      echo "OK: add-apt-repo contrib"
   fi
else
   echo "OK: cloudera contrib already in repo"
fi

grep "deb-src http://archive.cloudera.com/debian ${RELEASE}-cdh3 contrib" /etc/apt/sources.list >/dev/null
if [ $? -eq 1 ]; then 
   add-apt-repository "deb-src http://archive.cloudera.com/debian ${RELEASE}-cdh3 contrib"
   if [ $? -ne 0 ]; then 
      echo "ERROR: add-apt-repository failed to run for deb-src cloudera."
      exit 1
   else
      echo "OK: ads-apt-repo src"
   fi
else
   echo "OK: cloudera src contrib already in repo"
fi

apt-get update > /dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: apt-get update failed to run after addition of 'cloudera' repository."
   exit 1
else
   echo "OK: apt-get update cloudera repo"
fi

apt-get install -y curl  > /dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: apt-get install of curl failed to run."
   exit 1
else
  echo "OK: apt-get install curl"
fi

# optionally add the key
curl -s http://archive.cloudera.com/debian/archive.key | apt-key add -  > /dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: apt-key failed to install 'cloudera key'"
   exit 1
else
   echo "OK: add cloudera key"
fi

# hive server enable on Ubuntu - these are RHEL/CentOS'isms that are holdovers.
if [ -f /var/run/hive ]; then  
   echo "OK: /var/run/hive already exists."
else
   mkdir /var/run/hive > /dev/null
fi
if [ -f /var/lock/subsys ]; then  
   echo "OK: /var/lock/subsys already exists."
else
   mkdir /var/lock/subsys > /dev/null
fi

apt-get install -y --force-yes hadoop-0.20-conf-pseudo hadoop-hive hadoop-pig hadoop-zookeeper hadoop-hive-server hadoop-zookeeper-server sqoop oozie flume flume-master flume-node >/dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: apt-get install of hadoop and hadoop related components."
   exit 1
else
      echo "OK: hadoop installed"
fi

# write server public key to hdfs/.ssh/known_hosts
rm -rf  ~hdfs/.ssh > /dev/null
mkdir ~hdfs/.ssh > /dev/null
chown -R hdfs:hdfs  ~hdfs/.ssh > /dev/null
chmod 0700 ~hdfs/.ssh > /dev/null
# put the host (localhost) public key into the known_hosts file
su - hdfs -c 'ssh-keyscan -H -t rsa localhost  > ~hdfs/.ssh/known_hosts' > /dev/null
# gen user keypair  
su - hdfs -c 'ssh-keygen -q -N ""    -f  ~hdfs/.ssh/id_rsa'
# put the user's public key into it's authorized_keys to permit self login to localhost - necessary in hadoop
su - hdfs -c 'cp ~hdfs/.ssh/id_rsa.pub  ~hdfs/.ssh/authorized_keys'

# setup pig-env.sh
if [ -f /etc/pig/conf/pig-env.sh ]; then  
   echo "INFO: pig-env.sh already exists."
else
   echo 'JAVA_HOME=/usr/lib/jvm/java-6-sun; PIG_OPTS="$PIG_OPTS -Djava.library.path=/usr/lib/hadoop/lib/native/Linux-amd64-64"'  > /etc/pig/conf/pig-env.sh 
   chmod 0755 /etc/pig/conf/pig-env.sh 
fi

# fix JAVA_HOME
echo "export JAVA_HOME=/usr/lib/jvm/java-1.6.0-openjdk" >> /usr/lib/hadoop-0.20/conf/hadoop-env.sh

# format nodename storage directory
mkdir /var/lib/hadoop-0.20/cache/hadoop/dfs/name
chown hdfs:hadoop /var/lib/hadoop-0.20/cache/hadoop/dfs/name
echo 'Y' | su - hdfs -c '/usr/lib/hadoop-0.20/bin/hadoop namenode -format'

# start the cluster:
su - hdfs -c /usr/lib/hadoop-0.20/bin/start-all.sh > /dev/null

# sleep for 60 seconds because hadoop safemode for at least first 30-45 seconds.
sleep 60

# verify the cluster is alive
su - hdfs -c 'hadoop  dfsadmin -report' >/dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop dfsadmin -report failed to run."
   exit 1
else
      echo "OK: hadoop dfsadmin"
fi

su - hdfs -c 'hadoop fsck /'  > /dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop fsck failed to run."
   exit 1
else
      echo "OK: hadoop fsck"
fi

#-------------------------------
# Try performing some DFS 
# operations to confirm Hadoop 
# is working by performing some 
# operations and running a job.
#-------------------------------

su - hdfs -c 'hadoop fs -mkdir /foo' > /dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop mkdir failed to run."
   exit 1
else
      echo "OK: hadoop fs mkdir"
fi

su - hdfs -c 'hadoop fs -ls /' > /dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop ls failed to run."
   exit 1
else
      echo "OK: hadoop fs ls"
fi

su - hdfs -c 'hadoop fs -rmr /foo' > /dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop rmr failed to run."
   exit 1
else
      echo "OK: hadoop fs rmr"
fi

#-------------------------------
# Try to run an example job, 
# test the http ports and 
# validate it is working.
#-------------------------------

# jobtracker
curl -s --connect-timeout 5 --max-time 5 -o /dev/null -f http://localhost:50030/jobtracker.jsp
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop jobtracker webcheck failed."
   exit 1
else
      echo "OK: hadoop web jobtracker"
fi

# task tracker
curl -s --connect-timeout 5 --max-time 5 -o /dev/null -f http://localhost:50060/tasktracker.jsp
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop tasktracker webcheck failed."
   exit 1
else
      echo "OK: hadoop web tasktracker"
fi

# dfshealth - lists namenode, similar to "dsfadmin -report" listing
curl -s --connect-timeout 5 --max-time 5 -o /dev/null -f http://localhost:50070/dfshealth.jsp
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop dfshealth webcheck failed."
   exit 1
else
      echo "OK: hadoop dfshealt web"
fi

# web directory browser - WEB-INF  /
curl -s --connect-timeout 5 --max-time 5 -o /dev/null -f http://localhost:50075 
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop WEB-INF webcheck failed."
   exit 1
else
      echo "OK: hadoop WEB-INF"
fi

# secondary namenode
curl -s --connect-timeout 5 --max-time 5 -o /dev/null -f http://localhost:50090/status.jsp
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop secondary namenode webcheck failed."
   exit 1
else
      echo "OK: hadoop secondary namenode"
fi

# flume master
curl -s --connect-timeout 5 --max-time 5 -o /dev/null -f http://localhost:35871/flumemaster.jsp
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop flumemaster webcheck failed."
   exit 1
else
      echo "OK: hadoop flume master"
fi

# flume debugging - stack trace listing - NOTE: be sure to use the trailing '/' after "stacks"
curl -s --connect-timeout 5 --max-time 5 -o /dev/null -f http://localhost:35862/stacks/ 
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop flume stacks webcheck failed."
   exit 1
else
      echo "OK: hadoop flume stacks"
fi

# flume node - note that the port is the same as above only the called script differes
curl -s --connect-timeout 5 --max-time 5 -o /dev/null -f http://localhost:35862/flumeagent.jsp
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop flume agent webcheck failed."
   exit 1
else
      echo "OK: hadoop flume agent"
fi

# hive - note that this needs to run as root
hive -e "show databases;" > /dev/null
if [ $? -ne 0 ]; then 
   echo "ERROR: hadoop hive test failed."
   exit 1
else
      echo "OK: hadoop hive check "
fi

_eof

text_template hadoop_result_template

Thank you for provisioning a hadoop node setup.

You can login to the master server directly via SSH by connecting
to root@{{ hadoop_server.ipaddress_public }} using the password:

{{ hadoop_server_password_key.password }}

You can also access the status of your HDFS cluster 
on the web at the following URL:

http://{{ hadoop_server.ipaddress_public }}:50070/

_eof
