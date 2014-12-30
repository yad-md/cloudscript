cloudscript sugarcrm
    version                 = _latest
    result_template         = sugarcrm_result_template

globals
    server_password         = lib::random_password()
    console_password        = lib::random_password()
    sugarcrm_db_password    = lib::random_password()
    sugarcrm_db_name        = 'sugarcrm'
    sugarcrm_db_username    = 'sugarcrm'


thread core
    tasks                   = [install]
    
task install
    /key/password sugarcrm_server_key read_or_create
        key_group           = _SERVER
        password            = server_password
    
    /key/password sugarcrm_console_key read_or_create
        key_group           = _CONSOLE
        password            = console_password
    #
    # create sugarcrm storage slice, bootstrap script and recipe
    #
    
    # storage slice key
    /key/token sugarcrm_slice_key read_or_create
        username            = 'sugarcrmsliceuser'

    # slice
    /storage/slice sugarcrm_slice read_or_create
        keys                = [sugarcrm_slice_key]

    # slice container
    /storage/container sugarcrm_container => [sugarcrm_slice] read_or_create
        slice               = sugarcrm_slice

    # store script as object in cloudstorage
    /storage/object install_sugarcrm_script_object => [sugarcrm_slice, sugarcrm_container] read_or_create
        container_name      = 'sugarcrm_container'
        file_name           = 'install_sugarcrm.sh'
        slice               =  sugarcrm_slice
        content_data        =  install_sugarcrm_sh

    # associate the cloudstorage object with the sugarcrm script
    /orchestration/script install_sugarcrm_script => [sugarcrm_slice, sugarcrm_container, install_sugarcrm_script_object] read_or_create
        data_uri            = 'cloudstorage://sugarcrm_slice/sugarcrm_container/install_sugarcrm.sh'
        script_type         = _SHELL
        encoding            = _STORAGE

    # create the recipe and associate the script
    /orchestration/recipe install_sugarcrm_recipe read_or_create
        scripts             = [install_sugarcrm_script]

    # sugarcrm node
    /server/cloud sugarcrm_server read_or_create
        hostname            = 'sugarcrm'
        image               = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type        = 'CS05'
        keys                = [sugarcrm_server_key, sugarcrm_console_key]
        recipes             = [install_sugarcrm_recipe]

text_template sugarcrm_result_template

Thank you for provisioning a SugarCRM service.

You can now finish its configuration on the following page:

http://{{ sugarcrm_server.ipaddress_public }}/sugarcrm/install.php

Please use following credentials for database configuration:

hostname: localhost
username: {{ sugarcrm_db_username }}
password: {{ sugarcrm_db_password }}
database: {{ sugarcrm_db_name }}


You can also login to the server directly via SSH by connecting
to root@{{ sugarcrm_server.ipaddress_public }} using the password:

{{ sugarcrm_server_key.password }}

_eof

text_template install_sugarcrm_sh
#!/bin/bash
#
# install sugarcrm service
#

# check permissions
[ `whoami` = 'root' ] || {
    echo "ERROR: must have root permissions to execute the commands"
    exit 1
}

apt-get update > /dev/null
[ $? -eq 0 ] && echo "OK: update local apt cache" || {
    echo "ERROR: update local apt cache"
    exit 1
}

# install unzip
apt-get install -y unzip > /dev/null
[ $? -eq 0 ] && echo "OK: install unzip" || {
    echo "ERROR: install unzip"
    exit 1
}

# install MySQL
DEBIAN_FRONTEND=noninteractive apt-get install -y mysql-server mysql-client > /dev/null
[ $? -eq 0 ] && echo "OK: install MySQL" || {
    echo "ERROR: install MySQL"
    exit 1
}

# install Apache2
DEBIAN_FRONTEND=noninteractive apt-get install -y apache2 > /dev/null
[ $? -eq 0 ] && echo "OK: install Apache2" || {
    echo "ERROR: install Apache2"
    exit 1
}

# install PHP5
apt-get install -y php5 php5-cli libapache2-mod-php5 > /dev/null
[ $? -eq 0 ] && echo "OK: install PHP5" || {
    echo "ERROR: install PHP5"
    exit 1
}
# install required PHP5 modules
apt-get install -y php5-mysql php5-memcache php5-curl php5-gd php5-imap > /dev/null
[ $? -eq 0 ] && echo "OK: install PHP5 modules" || {
    echo "ERROR: install PHP5 modules"
    exit 1
}
# update php.ini
sed 's/^variables_order.*$/variables_order = "EGPCS"/' -i /etc/php5/apache2/php.ini
sed 's/^upload_max_filesize.*/upload_max_filesize = 10M/g' -i /etc/php5/apache2/php.ini
# enable PHP5 module
a2enmod php5 > /dev/null

# download sugarcrm source
cd /var/www && wget -O sugarce.zip "http://sourceforge.net/projects/sugarcrm/files/1%20-%20SugarCRM%206.5.X/SugarCommunityEdition-6.5.X/SugarCE-6.5.11.zip/download" > /dev/null 2>&1
[ $? -eq 0 ] && echo "OK: download sugarcrm source" || {
    echo "ERROR: download sugarcrm source"
    exit 1
}
# extract sugarcrm files
unzip sugarce.zip > /dev/null
[ $? -eq 0 ] && echo "OK: extract sugarcrm files" || {
    echo "ERROR: extract sugarcrm files"
    exit 1
}
# move sugarcrm files
cd /var/www/ && mv ./Sugar* ./sugarcrm
[ $? -eq 0 ] && echo "OK: move sugarcrm files" || {
    echo "ERROR: move sugarcrm files"
    exit 1
}
# set ownership & permissions
chown -R www-data:www-data /var/www/sugarcrm
[ $? -eq 0 ] && echo "OK: set required permissions" || {
    echo "ERROR: set required permissions"
    exit 1
}

# create MySQL database & user
mysql <<EOF
create database {{ sugarcrm_db_name }};
grant all on {{ sugarcrm_db_name }}.* to {{ sugarcrm_db_username }}@localhost identified by '{{ sugarcrm_db_password }}';
EOF
[ $? -eq 0 ] && echo "OK: create sugarcrm database and user" || {
    echo "ERROR: create sugarcrm database and user"
    exit 1
}

# enable mod_rewrite
a2enmod rewrite > /dev/null 2>&1

# restart web server
/etc/init.d/apache2 stop  > /dev/null 2>&1
/etc/init.d/apache2 start > /dev/null 2>&1

echo "OK: install sugarcrm service"

_eof

