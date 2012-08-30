cloudscript puppet_multi_stack
    version              = '2012-05-20'
    result_template      = puppet_pair_result_tmpl

globals
    server_password	     = lib::random_password()
    console_password     = lib::random_password()
    puppet_slice_user    = 'puppet'

thread puppet_setup
    tasks                = [puppet_master_agent_setup]
    
task puppet_master_agent_setup

    #-----------------------
    # Keys
    #-----------------------

    /key/password puppet_server_pass_key read_or_create
        key_group        = _SERVER
        password         = server_password        
    
    /key/password puppet_server_console_key read_or_create
        key_group        = _CONSOLE
        password         = console_password        

    # create storage slice keys
    /key/token puppet_slice_key read_or_create
        username         = puppet_slice_user   

    #-------------------------------
    # create puppet bootstrap
    #-------------------------------
    
    # create slice to store script in cloudstorage
    /storage/slice puppet_slice read_or_create
        keys                = [puppet_slice_key]
    
    # create slice container to store script in cloudstorage
    /storage/container puppet_container read_or_create
        slice               = puppet_slice
    
    # place script data in cloudstorage
    /storage/object puppet_master_script_object => [puppet_slice] read_or_create
        container_name      = 'puppet_container'
        file_name           = 'puppet_master_script.sh'
        slice               = puppet_slice
        content_data        = puppet_master_script_tmpl
        
    # associate the cloudstorage object with the puppet script
    /orchestration/script puppet_master_script => [puppet_slice, puppet_container, puppet_master_script_object] read_or_create
        data_uri            = 'cloudstorage://puppet_slice/puppet_container/puppet_master_script.sh'
        type                = _shell
        encoding            = _storage

    # place script data in cloudstorage
    /storage/object puppet_agent_script_object => [puppet_master_server, puppet_slice] read_or_create
        container_name      = 'puppet_container'
        file_name           = 'puppet_agent_script.sh'
        slice               = puppet_slice
        content_data        = puppet_agent_script_tmpl
        
    # associate the cloudstorage object with the puppet script
    /orchestration/script puppet_agent_script => [puppet_slice, puppet_container, puppet_agent_script_object] read_or_create
        data_uri            = 'cloudstorage://puppet_slice/puppet_container/puppet_agent_script.sh'
        type                = _shell
        encoding            = _storage
    
    #-------------------------------
    # create puppet master recipe
    #-------------------------------
    
    /orchestration/recipe puppet_master_recipe read_or_create
        scripts             = [puppet_master_script]

    /orchestration/recipe puppet_agent_recipe read_or_create
        scripts             = [puppet_agent_script]

    #-----------------------
    # Cloud Servers
    #-----------------------

    /server/cloud puppet_master_server read_or_create
        hostname         = 'puppet-master'
        image            = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        type             = 'CS1'
        keys             = [puppet_server_pass_key, puppet_server_console_key]
        recipes          = [puppet_master_recipe]

    /server/cloud puppet_agent_server read_or_create
        hostname         = 'puppet-agent'
        image            = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        type             = 'CS1'
        keys             = [puppet_server_pass_key, puppet_server_console_key]
        recipes          = [puppet_agent_recipe]

text_template puppet_agent_script_tmpl
#!/bin/bash

sudo apt-get update
sudo apt-get -y install puppet facter

echo "Using IP={{ puppet_master_server.ipaddress_private }} as IP for puppetmaster"

# setup client /etc/hosts
HOSTS_FILE='/etc/hosts'
 
LAN_IPADDR="{{ puppet_master_server.ipaddress_private }}"

# see if interface eth1 exists
if [ ! -z "${LAN_IPADDR}" ]; then 
    # see if puppet name already in hosts file
    grep -o "puppetmaster[ ]*$" ${HOSTS_FILE} 
    if [ $? -ne 0 ]; then
       # Divide the hosts file entry (if it exists) into the non-comment and comment portions
       IPENTRY=`grep ${LAN_IPADDR} ${HOSTS_FILE} | cut -d'#' -f1`
       COMMENT=`grep ${LAN_IPADDR} ${HOSTS_FILE} | grep -o "#[^$]*$"`
       if [ -z "${IPENTRY}" ]; then
          # ip was not listed in host file, let's put it there
          echo "${LAN_IPADDR} puppetmaster" >> ${HOSTS_FILE}
       else
          # ip was listed in host file so let's append to it
          sed -i -e "s/^${IPENTRY}/& puppetmaster ${COMMENT}/" ${HOSTS_FILE}
       fi
    else
       echo "INFO: 'puppetmaster' already defined in hosts file."
    fi
else
    echo "ERROR: Interface eth1 was not detected on puppet server install."
    echo "       Please report this error to technical support."
fi

# test agent connectivity - one shot
puppetd agent --test --server=puppetmaster --no-daemonize --verbose --onetime

puppetd agent --server=puppetmaster  --verbose

_eof

text_template puppet_master_script_tmpl
#!/bin/bash

sudo apt-get update
sudo apt-get install -y puppet puppetmaster facter

mkdir -p /etc/puppet/manifests
mkdir -p /etc/puppet/files

grep 'puppetserver' /etc/puppet/manifests/site.pp 
if [ $? -ne 0 ]; then
   echo '
import "nodes.pp"
$puppetserver="puppetmaster"
' > /etc/puppet/manifests/site.pp
fi

grep 'puppetagent' /etc/puppet/manifests/nodes.pp 
if [ $? -ne 0 ]; then
   echo '
node puppet-agent {

    include sudo
    package { "vim"    :  ensure => present, }
    package { "apache2":  ensure => present, }
    service { "apache2":  ensure => running, require => Package["apache2"], }

    file { "/var/www/index.html":
      content => "This web server was provisioned by puppet.",
      require => Package["apache2"],
      owner => "www-data",
      group => "www-data",
      mode => 0644,
    }


}
' > /etc/puppet/manifests/nodes.pp
fi

grep 'autosign=' /etc/puppet/puppet.conf 
if [ $? -ne 0 ]; then
   echo '# Note this is for demonstration pupposes only.  autosign set to true is bad from a security standpoint.' >> /etc/puppet/puppet.conf
   echo 'autosign=true' >> /etc/puppet/puppet.conf
fi

grep 'certname=' /etc/puppet/puppet.conf 
if [ $? -ne 0 ]; then
   echo 'certname=puppetmaster' >> /etc/puppet/puppet.conf
fi

# For our demo we'll force the agents to include sudo
mkdir -p /etc/puppet/modules/sudo/files
mkdir -p /etc/puppet/modules/sudo/templates
mkdir -p /etc/puppet/modules/sudo/manifests

# workaround to bug #2244 create a lib dir in any module - "pluginsync fails when no source is available"
mkdir -p /etc/puppet/modules/sudo/lib

touch /etc/puppet/modules/sudo/manifests/init.pp
grep 'class sudo' /etc/puppet/modules/sudo/manifests/init.pp 
if [ $? -ne 0 ]; then
   echo '
class sudo {
   package { sudo :
       ensure => present,
   }

   if $operatingsystem == "Ubuntu" {
      package { "sudo-ldap":
         ensure => present,
         require => Package["sudo"],
      }
   }

   file { "/etc/sudoers":
      owner => "root",
      group => "root",
      mode  => 0440,
      source => "puppet://$puppetserver/modules/sudo/etc/sudoers",
      require => Package["sudo"],
   }

}
' >> /etc/puppet/modules/sudo/manifests/init.pp
fi

mkdir -p /etc/puppet/modules/sudo/files/etc
# Note: be sure to check/sanitize this copy of /etc/sudoers
cp /etc/sudoers /etc/puppet/modules/sudo/files/etc
chmod 444 /etc/puppet/modules/sudo/files/etc/sudoers

# restart the master
service puppetmaster restart

_eof

text_template puppet_pair_result_tmpl

Thank you for provisioning a puppet master/agent setup.

You can login to the master server directly via SSH by connecting
to root@{{ puppet_master_server.ipaddress_public }} using the password:

{{ puppet_server_pass_key.password }}

You can login to the agent client directly via SSH by connecting
to root@{{ puppet_agent_server.ipaddress_public }} using the password:

{{ puppet_server_pass_key.password }}

_eof

